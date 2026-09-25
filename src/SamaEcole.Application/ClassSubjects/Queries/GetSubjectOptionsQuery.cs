using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.ClassSubjects.Queries;

/// <summary>
/// GET /api/v1/class-subjects/options?classroomId= | ?studentId= — la section « Matières optionnelles »
/// (Évolution N°6, étape C). Avec <see cref="ClassroomId"/> (formulaire d'inscription) : les groupes de la classe
/// choisie, option par défaut pré-cochée. Avec <see cref="StudentId"/> (fiche élève) : ceux de la classe de
/// l'élève pour l'année active, avec ses choix. Liste vide si la classe n'a aucun groupe : la section ne s'affiche pas.
/// </summary>
public record GetSubjectOptionsQuery(Guid? ClassroomId, Guid? StudentId) : IRequest<SubjectOptionsDto>;

/// <param name="ClassroomId">Classe dont viennent les groupes (celle de l'élève pour une fiche).</param>
public record SubjectOptionsDto(Guid? ClassroomId, Guid? SchoolYearId, IReadOnlyList<OptionGroupDto> Groups);

public class GetSubjectOptionsQueryValidator : AbstractValidator<GetSubjectOptionsQuery>
{
    public GetSubjectOptionsQueryValidator()
        => RuleFor(x => x)
            .Must(x => (x.ClassroomId is not null) ^ (x.StudentId is not null))
            .OverridePropertyName("Scope")
            .WithMessage("Précisez soit une classe, soit un élève — jamais les deux.");
}

public class GetSubjectOptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSubjectOptionsQuery, SubjectOptionsDto>
{
    public async Task<SubjectOptionsDto> Handle(GetSubjectOptionsQuery request, CancellationToken cancellationToken)
    {
        var yearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? classroomId;
        if (request.StudentId is { } studentId)
        {
            if (!await dbContext.Students.AnyAsync(s => s.Id == studentId, cancellationToken))
            {
                throw new KeyNotFoundException($"Élève {studentId} introuvable dans votre établissement.");
            }

            classroomId = yearId is null
                ? await dbContext.Students.AsNoTracking().Where(s => s.Id == studentId)
                    .Select(s => (Guid?)s.ClassroomId).FirstOrDefaultAsync(cancellationToken)
                : await StudentYearClassroom.ResolveAsync(dbContext, studentId, yearId.Value, cancellationToken);
        }
        else
        {
            classroomId = request.ClassroomId;
            if (!await dbContext.Classrooms.AnyAsync(c => c.Id == classroomId, cancellationToken))
            {
                throw new KeyNotFoundException($"Classe {classroomId} introuvable dans votre établissement.");
            }
        }

        var groups = classroomId is null
            ? []
            : await ClassOptionCatalog.LoadAsync(dbContext, classroomId.Value, yearId, request.StudentId, cancellationToken);

        return new SubjectOptionsDto(classroomId, yearId, groups);
    }
}
