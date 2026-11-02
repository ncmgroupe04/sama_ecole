using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.SearchStudentsForCashier;

/// <summary>
/// GET /finance/students/search — recherche élève pour la caisse (ticket écran Caisse). Reprend le
/// filtre nom/matricule de GetStudentsQuery, mais y ajoute directement le solde de l'inscription
/// active : la caissière doit voir, DANS LA LISTE de résultats, qu'un élève vient d'être inscrit par
/// le secrétariat mais n'a encore rien versé — sans avoir à sélectionner chaque élève un par un pour
/// le découvrir. Un élève venant d'être inscrit (AmountPaid = 0) ne doit pas se distinguer d'un élève
/// « pareil » qu'après ouverture de sa fiche : le signal doit être visible au premier coup d'œil.
///
/// Route DÉDIÉE à la Finance plutôt qu'un champ ajouté à GetStudentsQuery (Élèves, Enseignants…) :
/// le solde de l'inscription active n'intéresse que cet écran, et l'ajouter à la liste générique
/// aurait alourdi une sous-requête pour tous ses autres usages (bulletins, effectifs...).
///
/// LECTURE ouverte à tout rôle de l'école qui tient une caisse (même garde que GetStudentBalanceQuery) —
/// composer un solde n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record SearchStudentsForCashierQuery(string Search) : IRequest<IReadOnlyList<CashierStudentSearchResultDto>>;

public record CashierStudentSearchResultDto(
    Guid Id,
    string Matricule,
    string FullName,
    string? ClassroomName,

    /// <summary>Faux si l'élève n'a aucune inscription vivante sur l'année scolaire ACTIVE — rien à
    /// encaisser pour lui tant qu'il n'est pas (ré)inscrit ; les champs de solde valent alors 0.</summary>
    bool HasActiveEnrollment,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,

    /// <summary>
    /// MONTANT ÉCHU à ce jour, à encaisser en une fois au guichet : l'engagement initial (frais
    /// ponctuels + premier mois de scolarité) PLUS les mensualités dont la date d'échéance est
    /// atteinte, MOINS ce qui a déjà été versé — cf. InstallmentScheduleCalculator.IsDueNow, la
    /// source de vérité unique, la même que GetStudentBalanceQuery. C'est ce chiffre — et jamais
    /// <see cref="TotalDue"/>, le cumul annuel — que le badge de recherche affiche : sans les mois
    /// échus, un élève ayant réglé son inscription mais pas ses mensualités apparaissait « à jour ».
    /// </summary>
    decimal DueNowTotal);

public class SearchStudentsForCashierQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<SearchStudentsForCashierQuery, IReadOnlyList<CashierStudentSearchResultDto>>
{
    public async Task<IReadOnlyList<CashierStudentSearchResultDto>> Handle(
        SearchStudentsForCashierQuery request, CancellationToken cancellationToken)
    {
        var search = request.Search.Trim().ToLower();
        if (search.Length == 0) return [];

        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => new { y.Id, y.StartDate })
            .FirstOrDefaultAsync(cancellationToken);

        // Même remarque que GetStudentsQueryHandler : ToLower() plutôt qu'EF.Functions.ILike (Npgsql),
        // pour qu'Application reste ignorante du provider (Clean Architecture).
        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.FullName.ToLower().Contains(search) || s.Matricule.ToLower().Contains(search))
            .OrderBy(s => s.FullName)
            .Take(20)
            .Select(s => new
            {
                s.Id,
                s.Matricule,
                s.FullName,
                s.ClassroomId
            })
            .ToListAsync(cancellationToken);

        if (students.Count == 0) return [];

        var studentIds = students.Select(s => s.Id).ToList();

        // Une seule inscription VIVANTE par élève et par année (contrainte métier) : au plus une ligne
        // par StudentId ici, donc un simple dictionnaire suffit — pas besoin de désambiguïser.
        var enrollmentsById = activeYear is { } year
            ? await dbContext.Enrollments.AsNoTracking()
                .Where(e => studentIds.Contains(e.StudentId) && e.SchoolYearId == year.Id && e.Status != EnrollmentStatus.Cancelled)
                .Select(e => new { e.Id, e.StudentId, e.TotalDue, e.AmountPaid })
                .ToDictionaryAsync(e => e.StudentId, cancellationToken)
            : [];

        var classroomIds = students.Select(s => s.ClassroomId).Distinct().ToList();
        var classroomNamesById = await dbContext.Classrooms.AsNoTracking()
            .Where(c => classroomIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        // Échéances par inscription — le MÊME calcul que GetStudentBalanceQuery (allocation du cumul versé
        // échéance par échéance, plan personnalisé ACTIF prioritaire sur la synthèse mensuelle), pour que
        // le badge de recherche et le solde ouvert ensuite ne puissent jamais diverger. Peu de lignes
        // (≤ 20 inscriptions) : le regroupement se fait en mémoire.
        var dueNowByEnrollmentId = new Dictionary<Guid, decimal>();
        var enrollmentIds = enrollmentsById.Values.Select(e => e.Id).ToList();
        if (enrollmentIds.Count > 0 && activeYear is not null)
        {
            var feeLinesByEnrollment = (await dbContext.EnrollmentFeeLines.AsNoTracking()
                    .Where(l => enrollmentIds.Contains(l.EnrollmentId))
                    .OrderBy(l => l.IsRecurring)
                    .ThenBy(l => l.Designation)
                    .ToListAsync(cancellationToken))
                .GroupBy(l => l.EnrollmentId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<EnrollmentFeeLine>)g.ToList());

            var customInstallmentsByEnrollment = (await (
                    from p in dbContext.FeeInstallmentPlans.AsNoTracking()
                    where enrollmentIds.Contains(p.EnrollmentId) && p.Status == FeeInstallmentPlanStatus.Active
                    join i in dbContext.FeeInstallments.AsNoTracking() on p.Id equals i.FeeInstallmentPlanId
                    orderby i.SequenceNo
                    select new { p.EnrollmentId, Installment = i })
                    .ToListAsync(cancellationToken))
                .GroupBy(x => x.EnrollmentId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<FeeInstallment>)g.Select(x => x.Installment).ToList());

            var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
            foreach (var enrollment in enrollmentsById.Values)
            {
                var installments = InstallmentScheduleCalculator.Calculate(
                    enrollment.AmountPaid,
                    today,
                    feeLinesByEnrollment.GetValueOrDefault(enrollment.Id) ?? [],
                    activeYear.StartDate,
                    customInstallmentsByEnrollment.GetValueOrDefault(enrollment.Id));

                dueNowByEnrollmentId[enrollment.Id] = installments.Where(i => i.IsDueNow).Sum(i => i.RemainingDue);
            }
        }

        return students
            .Select(s =>
            {
                var enrollment = enrollmentsById.GetValueOrDefault(s.Id);

                var dueNowTotal = enrollment is null ? 0m : dueNowByEnrollmentId.GetValueOrDefault(enrollment.Id, 0m);

                return new CashierStudentSearchResultDto(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    classroomNamesById.GetValueOrDefault(s.ClassroomId, "Classe supprimée"),
                    enrollment is not null,
                    enrollment?.TotalDue ?? 0m,
                    enrollment?.AmountPaid ?? 0m,
                    (enrollment?.TotalDue ?? 0m) - (enrollment?.AmountPaid ?? 0m),
                    dueNowTotal);
            })
            .ToList();
    }
}
