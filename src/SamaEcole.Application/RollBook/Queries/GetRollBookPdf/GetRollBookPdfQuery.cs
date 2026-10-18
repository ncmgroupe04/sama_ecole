using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.RollBook.Pdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.RollBook.Queries.GetRollBookPdf;

/// <summary>
/// GET /api/v1/roll-book/pdf — le cahier de roulement imprimable, pour l'inspection. Exactement UNE période :
/// <c>Month</c> (AAAA-MM : le cahier mensuel) ou <c>TermId</c> (la période d'évaluation : grilles mensuelles,
/// récapitulatif, puis synthèse du niveau pour la direction et le secrétariat). <c>Lang</c> : « fr », « ar » ou « bi » ;
/// absent, le bilingue pour une école Franco-Arabe, le français sinon.
/// </summary>
public record GetRollBookPdfQuery(Guid ClassroomId, string? Month, Guid? TermId, string? Lang)
    : IRequest<RollBookPdfFile>;

/// <summary>Le PDF et un nom de fichier SANS accent ni espace, sûr dans un en-tête HTTP.</summary>
public sealed record RollBookPdfFile(byte[] Content, string FileName);

public class GetRollBookPdfQueryValidator : AbstractValidator<GetRollBookPdfQuery>
{
    public GetRollBookPdfQueryValidator()
    {
        RuleFor(q => q.ClassroomId).NotEmpty().WithMessage("La classe est obligatoire.");

        RuleFor(q => q)
            .Must(q => (q.Month is not null) ^ (q.TermId is not null))
            .WithName("Period")
            .WithMessage("Indiquez soit un mois (AAAA-MM), soit une période d'évaluation — pas les deux, pas aucune.");

        RuleFor(q => q.Month)
            .Must(m => RollBookPeriods.TryParseMonth(m, out _, out _)).When(q => q.Month is not null)
            .WithMessage("Le mois doit être au format AAAA-MM (ex. 2026-11).");

        RuleFor(q => q.Lang)
            .Must(l => l is null || RollBookPdfLanguages.TryParse(l, out _))
            .WithMessage("La langue doit être « fr », « ar » ou « bi ».");
    }
}

public static class RollBookPdfLanguages
{
    public static bool TryParse(string? value, out RollBookPdfLanguage language)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "fr": language = RollBookPdfLanguage.French; return true;
            case "ar": language = RollBookPdfLanguage.Arabic; return true;
            case "bi": language = RollBookPdfLanguage.Bilingual; return true;
            default: language = default; return false;
        }
    }
}

public class GetRollBookPdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    RollBookEngine engine,
    RollBookScopeAuthorizer scope,
    ISender mediator,
    ISchoolLogoProvider imageProvider,
    IRollBookPdfGenerator pdfGenerator,
    TimeProvider timeProvider)
    : IRequestHandler<GetRollBookPdfQuery, RollBookPdfFile>
{
    public async Task<RollBookPdfFile> Handle(GetRollBookPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new
            {
                s.Name, s.NameAr, s.InspectionAcademie, s.InspectionAcademieAr,
                s.InspectionEducationFormation, s.InspectionEducationFormationAr, s.LogoUrl
            })
            .SingleAsync(cancellationToken);

        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .Where(s => s.SchoolId == schoolId)
            .Select(s => new { s.IsCoranModuleEnabled, s.OfficialStampUrl, s.DirectorSignatureUrl })
            .FirstOrDefaultAsync(cancellationToken);
        var bilingualEnabled = settings?.IsCoranModuleEnabled ?? false;

        var language = ResolveLanguage(request.Lang, bilingualEnabled);

        var (from, to, year, term) = await ResolvePeriodAsync(request, cancellationToken);

        // Portée : un Enseignant n'accède qu'à ses classes (le contrôleur ne lui ouvre déjà pas l'export).
        await scope.EnsureCanAccessClassroomAsync(request.ClassroomId, year.Id, cancellationToken);

        var books = new List<ClassRollBook>();
        foreach (var (start, end) in MonthSlices(from, to))
        {
            books.Add(await engine.BuildAsync(request.ClassroomId, year, start, end, cancellationToken));
        }

        // Alternance FR / AR de la classe (Bloc 4) : une ligne d'information, seulement pour une école Franco-Arabe.
        SectionPattern? pattern = null;
        if (bilingualEnabled)
        {
            pattern = (await mediator.Send(new GetClassroomSectionScheduleQuery(request.ClassroomId), cancellationToken)).Pattern;
        }

        var months = books.Select(ToMonth).ToList();
        var first = books[0];

        var termRows = term is null ? [] : BuildTermRows(books);
        var levelClasses = term is not null && CanSeeLevelSynthesis()
            ? await BuildLevelClassesAsync(first, year, from, to, cancellationToken)
            : [];

        var model = new RollBookPdfModel(
            language,
            school.Name, school.NameAr, school.InspectionAcademie, school.InspectionAcademieAr,
            school.InspectionEducationFormation, school.InspectionEducationFormationAr,
            year.Label, first.Name, first.Level, pattern,
            months, term?.Label, termRows, levelClasses,
            DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime));

        // Images facultatives (logo, signature du Directeur, cachet) : leur absence n'empêche jamais l'émission.
        var assets = new RollBookPdfAssets(
            await imageProvider.TryFetchAsync(school.LogoUrl, cancellationToken),
            await imageProvider.TryFetchAsync(settings?.DirectorSignatureUrl, cancellationToken),
            await imageProvider.TryFetchAsync(settings?.OfficialStampUrl, cancellationToken));

        var period = term is null ? request.Month! : $"Periode-{Slug(term.Label)}";
        return new RollBookPdfFile(
            pdfGenerator.Generate(model, assets),
            $"Cahier-de-roulement-{Slug(first.Name)}-{period}.pdf");
    }

    /// <summary>
    /// L'arabe et le bilingue exigent le module Franco-Arabe : une école hors module a un cahier français seul
    /// (spec §5). Sans demande explicite, le bilingue pour une école Franco-Arabe.
    /// </summary>
    private static RollBookPdfLanguage ResolveLanguage(string? requested, bool bilingualEnabled)
    {
        if (requested is null)
        {
            return bilingualEnabled ? RollBookPdfLanguage.Bilingual : RollBookPdfLanguage.French;
        }

        RollBookPdfLanguages.TryParse(requested, out var language);

        if (language != RollBookPdfLanguage.French && !bilingualEnabled)
        {
            throw new ValidationException([
                new ValidationFailure("Lang",
                    "Le cahier en arabe ou bilingue est réservé aux établissements Franco-Arabes (module à activer dans Paramètres › Modules).")
            ]);
        }

        return language;
    }

    private async Task<(DateOnly From, DateOnly To, SchoolYear Year, Term? Term)> ResolvePeriodAsync(
        GetRollBookPdfQuery request, CancellationToken cancellationToken)
    {
        if (request.TermId is { } termId)
        {
            var term = await dbContext.Terms.AsNoTracking().FirstOrDefaultAsync(t => t.Id == termId, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.TermId), "Cette période d'évaluation n'existe pas dans votre établissement.")
                ]);

            var year = await dbContext.SchoolYears.AsNoTracking().FirstAsync(y => y.Id == term.SchoolYearId, cancellationToken);
            return (term.StartDate, term.EndDate, year, term);
        }

        RollBookPeriods.TryParseMonth(request.Month, out var from, out var to);
        return (from, to, await engine.ResolveYearAsync(from, to, cancellationToken), null);
    }

    /// <summary>Découpe une période en tranches d'un mois civil : [max(début, 1er du mois), min(fin, dernier du mois)].</summary>
    public static IEnumerable<(DateOnly Start, DateOnly End)> MonthSlices(DateOnly from, DateOnly to)
    {
        var cursor = new DateOnly(from.Year, from.Month, 1);
        while (cursor <= to)
        {
            var last = cursor.AddMonths(1).AddDays(-1);
            yield return (cursor < from ? from : cursor, last > to ? to : last);
            cursor = cursor.AddMonths(1);
        }
    }

    private static RollBookPdfMonth ToMonth(ClassRollBook book)
    {
        var from = book.Days[0].Date;
        var to = book.Days[^1].Date;

        return new RollBookPdfMonth(
            from, to,
            book.Days.Select(d => new RollBookPdfDay(d.Date, d.IsClassDay, d.OffReason, d.Closure?.Label, d.Closure?.LabelAr)).ToList(),
            book.ActiveHalves,
            book.Students.Select(s => new RollBookPdfStudent(
                s.Student.FullName, s.Student.FullNameAr, s.Student.Matricule,
                s.Cells.Select(c => new RollBookPdfCell(c.Date, c.Half, c.Cell.Mark)).ToList(),
                s.Counts)).ToList(),
            book.ExpectedHalfDaysPerStudent);
    }

    /// <summary>Un même roster sur tous les mois : on aligne les élèves par identifiant.</summary>
    private static List<RollBookPdfTermRow> BuildTermRows(IReadOnlyList<ClassRollBook> books) =>
        books[0].Students
            .Select(student =>
            {
                var perMonth = books
                    .Select(b => b.Students.FirstOrDefault(s => s.Student.Id == student.Student.Id)?.Counts ?? AttendanceCounts.Zero)
                    .ToList();

                return new RollBookPdfTermRow(
                    student.Student.FullName, student.Student.FullNameAr, perMonth, AttendanceCounts.Sum(perMonth));
            })
            .ToList();

    /// <summary>
    /// Les statistiques d'un niveau regroupent d'AUTRES classes : réservées aux rôles qui ont droit aux statistiques
    /// (Directeur, Secrétariat, Super Admin). Le Surveillant exporte le cahier de la classe, pas la synthèse du niveau.
    /// </summary>
    private bool CanSeeLevelSynthesis() => currentUser.Role is Role.Directeur or Role.Secretariat or Role.SuperAdmin;

    private async Task<List<RollBookPdfLevelClass>> BuildLevelClassesAsync(
        ClassRollBook current, SchoolYear year, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var classroomIds = await dbContext.Classrooms.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var rows = new List<RollBookPdfLevelClass>();
        foreach (var id in classroomIds)
        {
            var book = await engine.BuildAsync(id, year, from, to, cancellationToken);
            if (book.Students.Count > 0 && string.Equals(book.Level, current.Level, StringComparison.CurrentCultureIgnoreCase))
            {
                rows.Add(new RollBookPdfLevelClass(book.Name, book.Students.Count, book.Totals, book.ExpectedHalfDays));
            }
        }

        return rows;
    }

    /// <summary>Nom de fichier sûr : lettres et chiffres ASCII, le reste devient « - ».</summary>
    public static string Slug(string value)
    {
        var folded = new string(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        var slug = new string(folded.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");

        return slug.Length == 0 ? "classe" : slug;
    }
}
