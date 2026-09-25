using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Application.Institutional;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Syllabus;

internal static class SyllabusRules
{
    public const string UnknownGrade = "Niveau inconnu : choisissez un niveau de la liste (CI, CP… Terminale).";

    public static bool IsKnownGrade(string? grade) => AgeNormTemplates.For(grade) is not null;

    public static async Task EnsureSubjectAsync(IApplicationDbContext dbContext, Guid subjectId, CancellationToken cancellationToken)
    {
        if (!await dbContext.Subjects.AnyAsync(s => s.Id == subjectId, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure("SubjectId", "La matière indiquée n'existe pas dans votre établissement.")]);
        }
    }

    public static async Task<int> NextOrderAsync(IApplicationDbContext dbContext, Guid subjectId, string grade, CancellationToken cancellationToken)
        => (await dbContext.SyllabusUnits.Where(u => u.SubjectId == subjectId && u.GradeLevel == grade)
            .Select(u => (int?)u.Order).MaxAsync(cancellationToken) ?? 0) + 1;
}

/// <summary>
/// POST /api/v1/syllabus/units — ajoute des unités au programme d'une matière pour un niveau (Évolution N°7). Saisie en
/// masse : un intitulé par ligne de <see cref="Titles"/>, ajoutés à la suite, dans l'ordre ; un intitulé déjà présent
/// est ignoré (jamais un doublon).
/// </summary>
public record AddSyllabusUnitsCommand(Guid SubjectId, string GradeLevel, string? Section, IReadOnlyList<string> Titles)
    : IRequest<int>, IAuditableRequest;

public class AddSyllabusUnitsCommandValidator : AbstractValidator<AddSyllabusUnitsCommand>
{
    public AddSyllabusUnitsCommandValidator()
    {
        RuleFor(x => x.SubjectId).NotEmpty();
        RuleFor(x => x.GradeLevel).Must(SyllabusRules.IsKnownGrade).WithMessage(SyllabusRules.UnknownGrade);
        RuleFor(x => x.Section).MaximumLength(120).NoHtml();
        RuleFor(x => x.Titles).NotEmpty().WithMessage("Saisissez au moins un chapitre.");
        RuleForEach(x => x.Titles).MaximumLength(200).NoHtml();
    }
}

public class AddSyllabusUnitsCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<AddSyllabusUnitsCommand, int>
{
    public async Task<int> Handle(AddSyllabusUnitsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        await SyllabusRules.EnsureSubjectAsync(dbContext, request.SubjectId, cancellationToken);

        var existing = (await dbContext.SyllabusUnits
                .Where(u => u.SubjectId == request.SubjectId && u.GradeLevel == request.GradeLevel)
                .Select(u => u.Title)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var order = await SyllabusRules.NextOrderAsync(dbContext, request.SubjectId, request.GradeLevel, cancellationToken);
        var section = string.IsNullOrWhiteSpace(request.Section) ? null : request.Section.Trim();
        var added = 0;

        foreach (var title in request.Titles.Select(t => t.Trim()).Where(t => t.Length > 0))
        {
            if (!existing.Add(title)) continue;

            dbContext.SyllabusUnits.Add(new SyllabusUnit
            {
                SchoolId = schoolId, SubjectId = request.SubjectId, GradeLevel = request.GradeLevel,
                Section = section, Title = title, Order = order++
            });
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>
/// POST /api/v1/syllabus/import-template — importe la trame nationale codée (SyllabusTemplates) d'une matière pour un
/// niveau. N'ajoute que les unités absentes (idempotente) ; 422 si aucune trame n'existe pour cette matière et ce niveau.
/// </summary>
public record ImportSyllabusTemplateCommand(Guid SubjectId, string GradeLevel) : IRequest<int>, IAuditableRequest;

public class ImportSyllabusTemplateCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<ImportSyllabusTemplateCommand, int>
{
    public async Task<int> Handle(ImportSyllabusTemplateCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var subjectName = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == request.SubjectId).Select(s => s.Name).FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([new ValidationFailure("SubjectId", "La matière indiquée n'existe pas dans votre établissement.")]);

        var template = SyllabusTemplates.For(subjectName, request.GradeLevel)
            ?? throw new ValidationException([
                new ValidationFailure("GradeLevel",
                    $"Aucune trame nationale n'est disponible pour {subjectName} en {request.GradeLevel} : saisissez les chapitres (un par ligne).")
            ]);

        var existing = (await dbContext.SyllabusUnits
                .Where(u => u.SubjectId == request.SubjectId && u.GradeLevel == request.GradeLevel)
                .Select(u => u.Title).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.CurrentCultureIgnoreCase);

        var order = await SyllabusRules.NextOrderAsync(dbContext, request.SubjectId, request.GradeLevel, cancellationToken);
        var added = 0;
        foreach (var unit in template.Units.Where(u => existing.Add(u.Title)))
        {
            dbContext.SyllabusUnits.Add(new SyllabusUnit
            {
                SchoolId = schoolId, SubjectId = request.SubjectId, GradeLevel = request.GradeLevel,
                Section = unit.Section, Title = unit.Title, Order = order++, PlannedHours = unit.PlannedHours
            });
            added++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return added;
    }
}

/// <summary>PUT /api/v1/syllabus/units/{id} — corrige une unité (partie, intitulé, rang, volume horaire). 409 si périmée.</summary>
public record UpdateSyllabusUnitCommand : IRequest<Unit>, IAuditableRequest
{
    public Guid Id { get; init; }
    public string? Section { get; init; }
    public required string Title { get; init; }
    public int Order { get; init; }
    public decimal? PlannedHours { get; init; }
    public required uint RowVersion { get; init; }
}

public class UpdateSyllabusUnitCommandValidator : AbstractValidator<UpdateSyllabusUnitCommand>
{
    public UpdateSyllabusUnitCommandValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(x => x.Section).MaximumLength(120).NoHtml();
        RuleFor(x => x.Order).GreaterThanOrEqualTo(0);
        RuleFor(x => x.PlannedHours).InclusiveBetween(0, 500).When(x => x.PlannedHours is not null);
    }
}

public class UpdateSyllabusUnitCommandHandler(IApplicationDbContext dbContext) : IRequestHandler<UpdateSyllabusUnitCommand, Unit>
{
    public async Task<Unit> Handle(UpdateSyllabusUnitCommand request, CancellationToken cancellationToken)
    {
        var unit = await dbContext.SyllabusUnits.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Chapitre {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(unit, request.RowVersion);
        unit.Section = string.IsNullOrWhiteSpace(request.Section) ? null : request.Section.Trim();
        unit.Title = request.Title.Trim();
        unit.Order = request.Order;
        unit.PlannedHours = request.PlannedHours;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}

/// <summary>
/// DELETE /api/v1/syllabus/units/{id}?rowVersion= — archive une unité (suppression logique, règle #6). Les séances qui
/// l'avaient pointée restent au cahier de texte ; l'unité sort simplement du calcul d'avancement.
/// </summary>
public record DeleteSyllabusUnitCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class DeleteSyllabusUnitCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteSyllabusUnitCommand, Unit>
{
    public async Task<Unit> Handle(DeleteSyllabusUnitCommand request, CancellationToken cancellationToken)
    {
        var unit = await dbContext.SyllabusUnits.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Chapitre {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(unit, request.RowVersion);
        unit.SoftDelete(currentUser.UserId?.ToString() ?? "system");
        await dbContext.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
