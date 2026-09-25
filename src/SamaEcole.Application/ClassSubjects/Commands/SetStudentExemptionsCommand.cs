using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Exemptions;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// PUT /api/v1/class-subjects/students/{studentId}/exemptions — REMPLACE l'ensemble des dispenses de matières
/// obligatoires de l'élève pour l'année ACTIVE (liste vide = plus aucune). Tout est validé AVANT la moindre écriture ;
/// retrait par suppression logique (règle #6) ; idempotent. Le motif est obligatoire, nettoyé, jamais journalisé.
/// </summary>
public record SetStudentExemptionsCommand(Guid StudentId, IReadOnlyList<SubjectExemptionInput> Exemptions)
    : IRequest<Unit>, IAuditableRequest;

public class SetStudentExemptionsCommandValidator : AbstractValidator<SetStudentExemptionsCommand>
{
    public SetStudentExemptionsCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.Exemptions).NotNull();

        // Le motif est saisi librement puis affiché : pas de HTML. Sa présence et sa longueur sont vérifiées par
        // ExemptionRules (message par matière), pas ici.
        RuleForEach(x => x.Exemptions).ChildRules(exemption => exemption.RuleFor(e => e.Reason).NoHtml());
    }
}

public class SetStudentExemptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<SetStudentExemptionsCommand, Unit>
{
    public async Task<Unit> Handle(SetStudentExemptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var actor = (currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.")).ToString();

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);
        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, request.StudentId, yearId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "Cet élève n'est rattaché à aucune classe.")
            ]);

        var dispensable = await ExemptionQueries.DispensableAsync(dbContext, classroomId, cancellationToken);
        if (ExemptionRules.Validate(dispensable, request.Exemptions) is { } message)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Exemptions), message)]);
        }

        var wanted = request.Exemptions.ToDictionary(e => e.SubjectId, e => e.Reason!.Trim());

        var existing = await dbContext.StudentSubjectExemptions
            .Where(x => x.StudentId == request.StudentId && x.SchoolYearId == yearId)
            .ToListAsync(cancellationToken);

        foreach (var stale in existing.Where(x => !wanted.ContainsKey(x.SubjectId)))
        {
            stale.SoftDelete(actor);
        }

        foreach (var row in existing.Where(x => wanted.ContainsKey(x.SubjectId) && x.Reason != wanted[x.SubjectId]))
        {
            row.Reason = wanted[row.SubjectId];
        }

        var present = existing.Select(x => x.SubjectId).ToHashSet();
        foreach (var (subjectId, reason) in wanted.Where(w => !present.Contains(w.Key)))
        {
            dbContext.StudentSubjectExemptions.Add(new StudentSubjectExemption
            {
                SchoolId = schoolId, StudentId = request.StudentId, SubjectId = subjectId,
                SchoolYearId = yearId, Reason = reason
            });
        }

        // Une violation de l'index unique (deux secrétaires en même temps) est traduite par SaveChangesAsync en
        // ConcurrencyConflictException → 409, jamais un doublon silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
