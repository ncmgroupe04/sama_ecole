using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Enrollments.Commands.SetEnrollmentOptions;

/// <summary>
/// PUT /api/v1/enrollments/{id}/options — enregistre, pour l'année de l'inscription, deux choses INDÉPENDANTES :
/// — <see cref="SubjectIds"/> : les options que l'élève SUIT ; ses dispenses d'options deviennent toutes les
///   autres options du niveau de sa classe COURANTE (spec §5.2, écart E4) ;
/// — <see cref="Exemptions"/> : les matières OBLIGATOIRES dont il est dispensé, chacune avec son motif.
/// <c>null</c> = « ne pas toucher à cette moitié » ; une liste (même vide) la REMPLACE dans une transaction
/// (retrait par suppression logique, règle #6). Idempotent. Refusé (422) sur une inscription annulée ou qui
/// n'est pas celle de l'année active.
///
/// Les deux moitiés se distinguent par le motif : une ligne SANS motif est une option non suivie, une ligne
/// AVEC motif une matière obligatoire dispensée.
///
/// Le motif peut être médical : il n'est jamais journalisé et n'est renvoyé que par GetEnrollmentOptionsQuery.
/// </summary>
public record SetEnrollmentOptionsCommand(
    Guid EnrollmentId,
    IReadOnlyList<Guid>? SubjectIds,
    IReadOnlyList<MandatoryExemption>? Exemptions = null)
    : IRequest<Unit>, IAuditableRequest;

public class SetEnrollmentOptionsCommandValidator : AbstractValidator<SetEnrollmentOptionsCommand>
{
    public SetEnrollmentOptionsCommandValidator()
    {
        RuleFor(x => x.EnrollmentId).NotEmpty();

        // Le motif est saisi librement puis affiché : pas de HTML. Sa présence et sa longueur sont vérifiées
        // par OptionSelectionRules (message par matière), pas ici.
        RuleForEach(x => x.Exemptions).ChildRules(exemption =>
            exemption.RuleFor(e => e.Reason).NoHtml());
    }
}

public class SetEnrollmentOptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<SetEnrollmentOptionsCommand, Unit>
{
    public async Task<Unit> Handle(SetEnrollmentOptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : une inscription d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var enrollment = await dbContext.Enrollments.AsNoTracking()
            .Where(e => e.Id == request.EnrollmentId)
            .Select(e => new { e.Id, e.StudentId, e.SchoolYearId, e.Status })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        if (enrollment.Status == EnrollmentStatus.Cancelled)
        {
            throw Invalid(nameof(request.EnrollmentId),
                "Cette inscription est annulée : ses options ne peuvent plus être modifiées.");
        }

        if (!await dbContext.SchoolYears.AnyAsync(y => y.Id == enrollment.SchoolYearId && y.IsActive, cancellationToken))
        {
            throw Invalid(nameof(request.EnrollmentId),
                "Seules les options de l'inscription de l'année scolaire active peuvent être modifiées.");
        }

        var level = await CurrentLevelAsync(enrollment.StudentId, cancellationToken);

        // Tout est validé AVANT la moindre écriture : un choix refusé n'écrit rien.
        var optionTargets = request.SubjectIds is null
            ? null
            : await EnrollmentOptionsPlanner.PlanExemptionsAsync(
                dbContext, level, request.SubjectIds, nameof(request.SubjectIds), cancellationToken);

        var mandatoryTargets = request.Exemptions is null
            ? null
            : await EnrollmentOptionsPlanner.PlanMandatoryExemptionsAsync(
                dbContext, level, request.Exemptions, nameof(request.Exemptions), cancellationToken);

        var existing = await dbContext.EnrollmentSubjectExemptions
            .Where(x => x.EnrollmentId == enrollment.Id)
            .ToListAsync(cancellationToken);
        var actor = actorId.ToString();
        var wanted = mandatoryTargets?.ToDictionary(x => x.SubjectId, x => x.Reason!);

        // Phase 1 — RETRAIT des deux moitiés, décidé sur l'état d'origine. Moitié « options » = lignes SANS
        // motif absentes du choix (choix changé, matière d'un ancien niveau, matière devenue obligatoire) ;
        // moitié « matières obligatoires » = lignes AVEC motif absentes de la liste. Suppression logique.
        if (optionTargets is not null)
        {
            foreach (var stale in existing.Where(x => x.Reason is null && !optionTargets.Contains(x.SubjectId)))
            {
                stale.SoftDelete(actor);
            }
        }

        if (wanted is not null)
        {
            foreach (var stale in existing.Where(x => x.Reason is not null && !wanted.ContainsKey(x.SubjectId)))
            {
                stale.SoftDelete(actor);
            }
        }

        // Phase 2 — AJOUT / mise à jour contre les lignes qui SURVIVENT. Une ligne retirée à la phase 1 (même
        // appel) compte comme absente : une matière qui change de moitié (option devenue obligatoire, ou
        // l'inverse) reçoit une ligne NEUVE au bon motif au lieu d'hériter d'une ligne supprimée, ce qui
        // perdrait silencieusement la dispense.
        var bySubject = existing.Where(x => !x.IsDeleted).ToDictionary(x => x.SubjectId);

        if (optionTargets is not null)
        {
            foreach (var subjectId in optionTargets.Where(id => !bySubject.ContainsKey(id)))
            {
                dbContext.EnrollmentSubjectExemptions.Add(new EnrollmentSubjectExemption
                {
                    SchoolId = schoolId, EnrollmentId = enrollment.Id, SubjectId = subjectId
                });
            }
        }

        if (wanted is not null)
        {
            foreach (var (subjectId, reason) in wanted)
            {
                if (bySubject.TryGetValue(subjectId, out var row))
                {
                    if (row.Reason != reason)
                    {
                        row.Reason = reason;
                    }
                }
                else
                {
                    dbContext.EnrollmentSubjectExemptions.Add(new EnrollmentSubjectExemption
                    {
                        SchoolId = schoolId, EnrollmentId = enrollment.Id, SubjectId = subjectId, Reason = reason
                    });
                }
            }
        }

        // Une violation de l'index unique (deux secrétaires en même temps) est traduite par
        // SaveChangesAsync en ConcurrencyConflictException → 409, jamais un doublon silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }

    /// <summary>Niveau de la classe COURANTE de l'élève (Student.ClassroomId) : c'est elle que lisent les feuilles de notes.</summary>
    private async Task<string> CurrentLevelAsync(Guid studentId, CancellationToken cancellationToken) =>
        await (from s in dbContext.Students.AsNoTracking()
               join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
               where s.Id == studentId
               select c.Level).FirstOrDefaultAsync(cancellationToken)
        ?? string.Empty;

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
