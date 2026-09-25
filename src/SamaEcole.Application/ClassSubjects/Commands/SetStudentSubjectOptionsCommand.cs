using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// PUT /api/v1/class-subjects/students/{studentId}/options — enregistre les options d'un élève pour l'année ACTIVE
/// (fiche élève, Évolution N°6 étape C). <see cref="ClassSubjectIds"/> est la liste COMPLÈTE des options
/// retenues : un groupe absent de la liste reste sans option. L'année est résolue serveur (règle #10).
/// </summary>
public record SetStudentSubjectOptionsCommand : IRequest<IReadOnlyList<Guid>>, IAuditableRequest
{
    public Guid StudentId { get; init; }
    public IReadOnlyList<Guid> ClassSubjectIds { get; init; } = [];
}

public class SetStudentSubjectOptionsCommandValidator : AbstractValidator<SetStudentSubjectOptionsCommand>
{
    public SetStudentSubjectOptionsCommandValidator()
    {
        RuleFor(x => x.StudentId).NotEmpty();
        RuleFor(x => x.ClassSubjectIds).NotNull();
    }
}

public class SetStudentSubjectOptionsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ICurrentUserService currentUser)
    : IRequestHandler<SetStudentSubjectOptionsCommand, IReadOnlyList<Guid>>
{
    public async Task<IReadOnlyList<Guid>> Handle(SetStudentSubjectOptionsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new KeyNotFoundException($"Élève {request.StudentId} introuvable dans votre établissement.");
        }

        var yearId = await CoefficientRules.ActiveSchoolYearIdAsync(dbContext, cancellationToken);

        var classroomId = await StudentYearClassroom.ResolveAsync(dbContext, request.StudentId, yearId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "Cet élève n'est rattaché à aucune classe.")
            ]);

        var final = await StudentOptionWriter.SetAsync(
            dbContext, schoolId, currentUser.UserId?.ToString() ?? "system",
            request.StudentId, classroomId, yearId, request.ClassSubjectIds, fillDefaults: false, cancellationToken,
            nameof(request.ClassSubjectIds));

        await dbContext.SaveChangesAsync(cancellationToken);
        return final;
    }
}
