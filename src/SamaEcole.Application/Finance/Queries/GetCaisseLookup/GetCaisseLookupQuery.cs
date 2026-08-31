using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetCaisseLookup;

/// <summary>
/// GET /api/v1/finance/caisse/lookup?query={studentId|matricule} — modèle hybride, volet 2.
///
/// Point d'entrée « détection de dette » de l'écran Caisse. L'élève est d'abord résolu par la Caisse
/// via GET /students?search (recherche par nom, UI existante) ; <see cref="Query"/> porte donc son
/// IDENTIFIANT (GUID) ou, par commodité, son MATRICULE EXACT — jamais une recherche par nom, qui
/// reste le rôle de GET /students.
///
/// La réponse porte le drapeau <c>HasPendingEnrollment</c> : vrai dès qu'il existe, sur l'année
/// active, une inscription en <see cref="EnrollmentStatus.PendingPayment"/> OU une inscription
/// confirmée dont le solde n'est pas nul. C'est lui qui déclenche la modale prioritaire de
/// recouvrement côté Caisse. Aucune inscription active ⇒ <c>HasPendingEnrollment = false</c> et
/// identité seule (jamais un 404 : la Caisse veut afficher « aucune dette » sans faire échouer sa
/// recherche). Élève introuvable ⇒ 404.
///
/// LECTURE : rôles de caisse (Directeur, Finance, Secrétariat — voir FinanceController.CashierRoles).
/// Composer/afficher un solde n'est pas un acte d'encaissement (règle #4) ; seul POST /finance/payments
/// l'est. Global Query Filter + policy RLS bornent déjà tout au tenant courant (règle #2).
/// </summary>
public record GetCaisseLookupQuery(string Query) : IRequest<CaisseLookupDto>;

/// <summary>
/// Ce que la Caisse affiche dans la modale de recouvrement. <paramref name="EnrollmentId"/> et les
/// montants sont null / 0 quand l'élève n'a aucune inscription active (rien à encaisser).
/// </summary>
public record CaisseLookupDto(
    Guid StudentId,
    string FullName,
    string Matricule,
    bool HasPendingEnrollment,
    Guid? EnrollmentId,
    decimal TotalDue,
    decimal AmountPaid,
    decimal BalanceRemaining,

    /// <summary>Statut de l'inscription (« PendingPayment », « Confirmed »…) ou null si aucune inscription active.</summary>
    string? Status,
    string? SchoolYearLabel,
    string? ClassroomName,

    /// <summary>Ventilation du dû ANNUEL, une ligne par frais du barème figé à l'inscription.</summary>
    IReadOnlyList<CaisseLookupFeeLine> Breakdown);

/// <summary>
/// Une ligne de la ventilation des frais. Miroir d'<c>EnrollmentFeeLineDto</c> : <paramref name="Amount"/>
/// est le total de la ligne sur l'année (mensualité × nombre de mois pour une ligne récurrente,
/// montant entier pour un frais ponctuel), et <paramref name="Months"/> permet à la modale d'afficher
/// « Mensualité (× 9) » sans recalcul.
/// </summary>
public record CaisseLookupFeeLine(
    string Category,
    bool IsRecurring,
    decimal UnitAmount,
    int Months,
    decimal Amount);

public class GetCaisseLookupQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetCaisseLookupQuery, CaisseLookupDto>
{
    public async Task<CaisseLookupDto> Handle(GetCaisseLookupQuery request, CancellationToken cancellationToken)
    {
        var needle = request.Query.Trim();

        // Résolution de l'élève : GUID (cas nominal, la Caisse a déjà choisi l'élève) ou matricule
        // exact (commodité). Insensible à la casse via ToLower() — jamais EF.Functions.ILike : la
        // couche Application ne connaît aucun provider (Clean Architecture), Npgsql traduit lower().
        var student = Guid.TryParse(needle, out var studentId)
            ? await dbContext.Students.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == studentId, cancellationToken)
            : await dbContext.Students.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Matricule.ToLower() == needle.ToLower(), cancellationToken);

        if (student is null)
        {
            throw new KeyNotFoundException(
                $"Aucun élève ne correspond à « {needle} » dans votre établissement.");
        }

        // Inscription de l'année ACTIVE, hors annulée. Le Global Query Filter + la RLS bornent la
        // sous-requête au tenant courant (règle #2) — inutile de refiltrer sur SchoolId ici.
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.StudentId == student.Id && y.IsActive && e.Status != EnrollmentStatus.Cancelled
            select new
            {
                e.Id,
                e.Status,
                e.TotalDue,
                e.AmountPaid,
                ClassroomName = c.Name,
                SchoolYearLabel = y.Label
            }
        ).FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Élève connu mais pas d'inscription vivante sur l'année active : rien à recouvrer.
            return new CaisseLookupDto(
                student.Id, student.FullName, student.Matricule,
                HasPendingEnrollment: false,
                EnrollmentId: null, TotalDue: 0m, AmountPaid: 0m, BalanceRemaining: 0m,
                Status: null, SchoolYearLabel: null, ClassroomName: null,
                Breakdown: []);
        }

        var balanceRemaining = row.TotalDue - row.AmountPaid;

        // La modale prioritaire s'ouvre pour toute dette d'inscription : soit le statut PendingPayment
        // (engagée au secrétariat, jamais passée en caisse), soit un solde non nul sur une inscription
        // déjà confirmée (règlement partiel antérieur).
        var hasPending = row.Status == EnrollmentStatus.PendingPayment || balanceRemaining > 0m;

        var breakdown = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == row.Id)
            .OrderBy(l => l.IsRecurring)
            .ThenBy(l => l.Designation)
            .Select(l => new CaisseLookupFeeLine(
                l.Designation, l.IsRecurring, l.UnitAmount, l.Months, l.LineTotal))
            .ToListAsync(cancellationToken);

        return new CaisseLookupDto(
            student.Id,
            student.FullName,
            student.Matricule,
            HasPendingEnrollment: hasPending,
            EnrollmentId: row.Id,
            TotalDue: row.TotalDue,
            AmountPaid: row.AmountPaid,
            BalanceRemaining: balanceRemaining,
            Status: row.Status.ToString(),
            SchoolYearLabel: row.SchoolYearLabel,
            ClassroomName: row.ClassroomName,
            Breakdown: breakdown);
    }
}
