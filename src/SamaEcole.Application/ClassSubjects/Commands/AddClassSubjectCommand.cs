using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Coefficients;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.ClassSubjects.Commands;

/// <summary>
/// POST /api/v1/class-subjects — ajoute au programme d'une classe une matière propre à l'établissement
/// (Informatique, Conduite…) : une matière EXISTANTE (<see cref="SubjectId"/>) ou une NOUVELLE, créée au niveau de
/// la classe (<see cref="NewSubjectName"/>) — exactement l'un des deux. <see cref="Coefficient"/>, s'il est donné et
/// diffère du coefficient hérité, devient la surcharge de classe de l'année active (moteur de l'Évolution N°4).
/// </summary>
public record AddClassSubjectCommand : IRequest<Guid>, IAuditableRequest
{
    public required Guid ClassroomId { get; init; }
    public Guid? SubjectId { get; init; }
    public string? NewSubjectName { get; init; }
    public decimal? Coefficient { get; init; }
    public string? OptionGroup { get; init; }
}

public class AddClassSubjectCommandValidator : AbstractValidator<AddClassSubjectCommand>
{
    public AddClassSubjectCommandValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();

        RuleFor(x => x)
            .Must(x => (x.SubjectId is not null) ^ !string.IsNullOrWhiteSpace(x.NewSubjectName))
            .OverridePropertyName("Subject")
            .WithMessage("Choisissez une matière existante ou saisissez le nom d'une nouvelle matière — pas les deux.");

        RuleFor(x => x.NewSubjectName).MaximumLength(80).NoHtml();
        RuleFor(x => x.OptionGroup).MaximumLength(40).NoHtml();

        RuleFor(x => x.Coefficient)
            .GreaterThan(0).WithMessage("Le coefficient doit être supérieur à zéro.")
            .LessThanOrEqualTo(CoefficientRules.MaxCoefficient)
                .WithMessage($"Le coefficient annoncé semble irréaliste (maximum {CoefficientRules.MaxCoefficient:0}).")
            .When(x => x.Coefficient is not null);
    }
}

public class AddClassSubjectCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<AddClassSubjectCommand, Guid>
{
    public async Task<Guid> Handle(AddClassSubjectCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == request.ClassroomId)
            .Select(c => new { c.Id, c.Level, c.Cycle, c.Series })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        if (classroom.Cycle.UsesSimplifiedGrading())
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId),
                    "Au primaire et en maternelle, le programme suit les matières du niveau : il ne se règle pas par classe.")
            ]);
        }

        Subject subject;
        if (request.SubjectId is { } subjectId)
        {
            subject = await dbContext.Subjects.FirstOrDefaultAsync(s => s.Id == subjectId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
                ]);
        }
        else
        {
            var name = request.NewSubjectName!.Trim();
            var existing = await dbContext.Subjects
                .Where(s => s.Level == classroom.Level && s.ParentSubjectId == null)
                .ToListAsync(cancellationToken);

            // Même matière déjà créée pour ce niveau (casse et accents ignorés) : on la réutilise plutôt que de
            // buter sur l'index unique (Niveau, Nom).
            var reused = existing.FirstOrDefault(s =>
                SeriesCoefficientTemplates.NormalizeName(s.Name) == SeriesCoefficientTemplates.NormalizeName(name));

            if (reused is not null)
            {
                subject = reused;
            }
            else
            {
                subject = new Subject
                {
                    SchoolId = schoolId,
                    Name = name,
                    Level = classroom.Level,
                    Coefficient = request.Coefficient ?? 1m
                };
                dbContext.Subjects.Add(subject);
            }
        }

        if (await dbContext.ClassSubjects.AnyAsync(
                c => c.ClassroomId == classroom.Id && c.SubjectId == subject.Id, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure("Subject",
                    "Cette matière figure déjà au programme de la classe : réactivez-la ou modifiez-la dans la liste.")
            ]);
        }

        var order = await dbContext.ClassSubjects
            .Where(c => c.ClassroomId == classroom.Id)
            .Select(c => (int?)c.DisplayOrder)
            .MaxAsync(cancellationToken) ?? -1;

        var row = new ClassSubject
        {
            SchoolId = schoolId,
            ClassroomId = classroom.Id,
            SubjectId = subject.Id,
            OptionGroup = SubjectFollowRules.NormalizeGroup(request.OptionGroup),
            IsCustom = true,
            IsActive = true,
            DisplayOrder = order + 1
        };
        dbContext.ClassSubjects.Add(row);

        if (request.Coefficient is { } coefficient)
        {
            await SetClassCoefficientAsync(schoolId, classroom.Id, classroom.Series, subject, coefficient, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return row.Id;
    }

    /// <summary>Surcharge de classe de l'année active, seulement si le coefficient demandé diffère de l'hérité.</summary>
    private async Task SetClassCoefficientAsync(
        Guid schoolId, Guid classroomId, string? series, Subject subject, decimal coefficient,
        CancellationToken cancellationToken)
    {
        var yearId = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => (Guid?)y.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (yearId is null)
        {
            return;
        }

        var overrides = await dbContext.SubjectCoefficientOverrides
            .Where(o => o.SchoolYearId == yearId && o.SubjectId == subject.Id
                        && (o.ClassroomId == classroomId || (series != null && o.Series == series)))
            .ToListAsync(cancellationToken);

        var classOverride = overrides.FirstOrDefault(o => o.ClassroomId == classroomId);
        var inherited = overrides.FirstOrDefault(o => o.ClassroomId is null)?.Coefficient ?? subject.Coefficient;

        if (classOverride is not null)
        {
            classOverride.Coefficient = coefficient;
        }
        else if (inherited != coefficient)
        {
            dbContext.SubjectCoefficientOverrides.Add(new SubjectCoefficientOverride
            {
                SchoolId = schoolId,
                SchoolYearId = yearId.Value,
                SubjectId = subject.Id,
                ClassroomId = classroomId,
                Coefficient = coefficient
            });
        }
    }
}
