using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.RollBook.Queries.GetRollBookStats;

/// <summary>
/// GET /api/v1/roll-book/stats — taux de présence, d'absence et d'assiduité d'une période.
///
/// <c>Scope</c> : « class » (élèves d'une classe, avec la classe et son niveau) ou « level » (toutes les classes
/// de l'école regroupées par niveau). Période : exactement UN de <c>Month</c> (AAAA-MM) ou <c>TermId</c>
/// (trimestre / semestre / période de l'année).
/// </summary>
public record GetRollBookStatsQuery(string Scope, Guid? ClassroomId, string? Month, Guid? TermId)
    : IRequest<RollBookStatsDto>;

public class GetRollBookStatsQueryValidator : AbstractValidator<GetRollBookStatsQuery>
{
    public GetRollBookStatsQueryValidator()
    {
        RuleFor(q => q.Scope)
            .Must(s => s is "class" or "level")
            .WithMessage("La portée doit être « class » ou « level ».");

        RuleFor(q => q.ClassroomId)
            .NotEmpty().When(q => q.Scope == "class")
            .WithMessage("La classe est obligatoire pour la portée « class ».");

        RuleFor(q => q)
            .Must(q => (q.Month is not null) ^ (q.TermId is not null))
            .WithName("Period")
            .WithMessage("Indiquez soit un mois (AAAA-MM), soit un trimestre — pas les deux, pas aucun.");

        RuleFor(q => q.Month)
            .Must(m => RollBookPeriods.TryParseMonth(m, out _, out _)).When(q => q.Month is not null)
            .WithMessage("Le mois doit être au format AAAA-MM (ex. 2026-11).");
    }
}

public class GetRollBookStatsQueryHandler(
    IApplicationDbContext dbContext, RollBookEngine engine, RollBookScopeAuthorizer scope)
    : IRequestHandler<GetRollBookStatsQuery, RollBookStatsDto>
{
    public async Task<RollBookStatsDto> Handle(GetRollBookStatsQuery request, CancellationToken cancellationToken)
    {
        var (from, to, year) = await ResolvePeriodAsync(request, cancellationToken);

        var books = new List<ClassRollBook>();

        if (request.Scope == "class")
        {
            await scope.EnsureCanAccessClassroomAsync(request.ClassroomId!.Value, year.Id, cancellationToken);
            books.Add(await engine.BuildAsync(request.ClassroomId.Value, year, from, to, cancellationToken));
        }
        else
        {
            var classroomIds = await dbContext.Classrooms.AsNoTracking()
                .OrderBy(c => c.Name)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            foreach (var id in classroomIds)
            {
                var book = await engine.BuildAsync(id, year, from, to, cancellationToken);

                // Une classe sans élève n'a ni taux ni poids : elle n'encombre pas la synthèse.
                if (book.Students.Count > 0)
                {
                    books.Add(book);
                }
            }
        }

        var students = request.Scope == "class"
            ? books[0].Students
                .Select(s => new RollBookStudentStatsDto(
                    s.Student.Id, s.Student.Matricule, s.Student.FullName, s.Student.FullNameAr,
                    RollBookCountsDto.From(s.Counts, books[0].ExpectedHalfDaysPerStudent)))
                .ToList()
            : [];

        var classes = books
            .Select(b => new RollBookClassStatsDto(
                b.ClassroomId, b.Name, b.Level, b.Students.Count, b.ClassDayCount,
                RollBookCountsDto.From(b.Totals, b.ExpectedHalfDays)))
            .ToList();

        // Niveau : compteurs SOMMÉS puis taux recalculés — jamais une moyenne de taux de classes.
        var levels = books
            .GroupBy(b => b.Level, StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new RollBookLevelStatsDto(
                g.First().Level,
                g.Count(),
                g.Sum(b => b.Students.Count),
                RollBookCountsDto.From(AttendanceCounts.Sum(g.Select(b => b.Totals)), g.Sum(b => b.ExpectedHalfDays))))
            .OrderBy(l => l.Level, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var totals = RollBookCountsDto.From(
            AttendanceCounts.Sum(books.Select(b => b.Totals)), books.Sum(b => b.ExpectedHalfDays));

        return new RollBookStatsDto(request.Scope, from, to, year.Id, year.Label, students, classes, levels, totals);
    }

    private async Task<(DateOnly From, DateOnly To, Domain.Entities.SchoolYear Year)> ResolvePeriodAsync(
        GetRollBookStatsQuery request, CancellationToken cancellationToken)
    {
        if (request.TermId is { } termId)
        {
            var term = await dbContext.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.TermId), "Cette période d'évaluation n'existe pas dans votre établissement.")
                ]);

            var termYear = await dbContext.SchoolYears.AsNoTracking()
                .FirstAsync(y => y.Id == term.SchoolYearId, cancellationToken);

            return (term.StartDate, term.EndDate, termYear);
        }

        RollBookPeriods.TryParseMonth(request.Month, out var from, out var to);
        return (from, to, await engine.ResolveYearAsync(from, to, cancellationToken));
    }
}
