using FluentAssertions;
using MediatR;
using SamaEcole.Application.Attendance;
using SamaEcole.Application.Attendance.Commands.SubmitAttendanceSheet;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.Reports;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Reports;

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

file sealed class NoOpPublish : IPublisher
{
    public Task Publish(object notification, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task Publish<TNotification>(TNotification notification, CancellationToken cancellationToken = default)
        where TNotification : INotification => Task.CompletedTask;
}

file sealed class StubTenant(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>Dispatch minimal : ReportCardDataService n'a besoin que de GetGradeSummaryQuery (pas de DI).</summary>
file sealed class GradeSummaryOnlyMediator(ApplicationDbContext dbContext) : ISender
{
    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
    {
        if (request is GetGradeSummaryQuery query)
        {
            return (Task<TResponse>)(object)new GetGradeSummaryQueryHandler(dbContext).Handle(query, cancellationToken);
        }

        throw new NotSupportedException($"Mediator de test : {request.GetType().Name} non routé.");
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>
/// Évolution N°3, exigence 3 — « les week-ends spécifiques ne comptent pas comme des jours de classe ».
///
/// Le calcul des taux NE CHANGE PAS : tous sont des rapports « présences ÷ lignes d'appel réellement
/// saisies », jamais des jours calendaires. Ces tests figent les propriétés qui rendent l'exigence vraie,
/// pour qu'une refonte future du calcul (ex. un dénominateur en jours calendaires) ne les casse pas
/// en silence :
///   1. un jour de repos SANS appel n'entre dans aucun dénominateur ;
///   2. un appel HISTORIQUE saisi un jour devenu repos reste compté (arbitrage D2) ;
///   3. le verrou empêche d'en ajouter un nouveau ;
///   4. l'assiduité du bulletin ne compte que les fiches existantes (null si aucune, jamais un zéro).
///
/// École à repos jeudi/vendredi. Les fiches sont posées par le contexte PROPRIÉTAIRE : les saisir via le
/// Handler serait refusé les jours de repos. 2026-09-19 = samedi, 09-20 = dimanche, 09-24 = jeudi.
/// </summary>
public class AttendanceRateWorkingDaysTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("6aaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Matiere = Guid.Parse("6ccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Annee = Guid.Parse("61111111-0000-0000-0000-000000000001");
    private static readonly Guid Eleve1 = Guid.Parse("6eeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid Eleve2 = Guid.Parse("6eeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid Trimestre1 = Guid.Parse("6ddddddd-0000-0000-0000-0000000000d1");
    private static readonly Guid Trimestre2 = Guid.Parse("6ddddddd-0000-0000-0000-0000000000d2");

    private static readonly DateOnly Debut = new(2026, 9, 19);
    private static readonly DateOnly Fin = new(2026, 9, 27);

    private static readonly TestCurrentUser Directeur = new(Guid.Parse("6d000000-0000-0000-0000-000000000001"), Role.Directeur);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = Ecole, WorkingDays = "Monday,Tuesday,Wednesday,Saturday,Sunday" });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true
        });
        owner.Terms.AddRange(
            new Term { Id = Trimestre1, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1, StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20) },
            new Term { Id = Trimestre2, SchoolId = Ecole, SchoolYearId = Annee, Label = "2e trimestre", Order = 2, StartDate = new DateOnly(2027, 1, 5), EndDate = new DateOnly(2027, 3, 20) });
        owner.Students.AddRange(
            new Student { Id = Eleve1, SchoolId = Ecole, Matricule = "ELEV-2026-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe },
            new Student { Id = Eleve2, SchoolId = Ecole, Matricule = "ELEV-2026-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 8, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = Classe });

        // Samedi 19 : 2 présents. Dimanche 20 : 1 présent, 1 absent non justifié. Aucune fiche jeudi/vendredi.
        AddSheet(owner, new DateOnly(2026, 9, 19), "Matin",
            (Eleve1, AttendanceStatus.Present), (Eleve2, AttendanceStatus.Present));
        AddSheet(owner, new DateOnly(2026, 9, 20), "Matin",
            (Eleve1, AttendanceStatus.Present), (Eleve2, AttendanceStatus.UnjustifiedAbsence));

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    // 1 — un jour de repos sans appel ne pèse dans aucun dénominateur.
    [Fact]
    public async Task A_Rest_Day_Without_A_Roll_Call_Does_Not_Weigh_On_The_Rate()
    {
        await using var context = _db.NewAppContext(Ecole);

        var aggregate = await new AttendanceReportAggregator(context).ComputeAsync(Debut, Fin, null, default);

        // 3 présents ÷ 4 lignes : les jeudi 24 et vendredi 25, sans appel, n'entrent nulle part.
        aggregate.AverageAttendanceRate.Should().Be(0.75m);
    }

    // 2 — un appel historique saisi un jour devenu repos reste compté (D2) : jamais exclu rétroactivement.
    [Fact]
    public async Task A_Historical_Roll_Call_On_A_Day_That_Became_A_Rest_Day_Is_Still_Counted()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            AddSheet(owner, new DateOnly(2026, 9, 24), "Matin",
                (Eleve1, AttendanceStatus.UnjustifiedAbsence), (Eleve2, AttendanceStatus.UnjustifiedAbsence));
            await owner.SaveChangesAsync();
        }

        await using var context = _db.NewAppContext(Ecole);

        var aggregate = await new AttendanceReportAggregator(context).ComputeAsync(Debut, Fin, null, default);

        aggregate.AverageAttendanceRate.Should().Be(0.5m); // 3 présents ÷ 6 lignes
    }

    // 3 — le verrou empêche d'ajouter un nouvel appel un jour de repos ; le taux ne bouge pas.
    [Fact]
    public async Task The_Lock_Prevents_A_New_Roll_Call_On_A_Rest_Day_And_The_Rate_Is_Unchanged()
    {
        await using var context = _db.NewAppContext(Ecole);
        var handler = new SubmitAttendanceSheetCommandHandler(
            context, new StubTenant(Ecole), Directeur, new AttendanceScopeAuthorizer(context, Directeur),
            new WorkingDayGuard(context), new NoOpPublish(), new NoOpKpiCache());

        var act = async () => await handler.Handle(
            new SubmitAttendanceSheetCommand
            {
                ClassroomId = Classe,
                SubjectId = Matiere,
                Date = new DateOnly(2026, 9, 25), // vendredi
                Period = "Matin",
                Entries = [new AttendanceEntry(Eleve1, AttendanceStatus.UnjustifiedAbsence, 0)]
            },
            default);

        await act.Should().ThrowAsync<ValidationException>();

        await using var relecture = _db.NewAppContext(Ecole);
        var aggregate = await new AttendanceReportAggregator(relecture).ComputeAsync(Debut, Fin, null, default);
        aggregate.AverageAttendanceRate.Should().Be(0.75m);
    }

    // 4 — assiduité du bulletin : uniquement les fiches existantes du trimestre ; null (« - ») s'il n'y en a aucune.
    [Fact]
    public async Task The_Report_Card_Attendance_Counts_Existing_Sheets_Only_And_Is_Null_Without_Any()
    {
        await using var context = _db.NewAppContext(Ecole);
        var service = new ReportCardDataService(new GradeSummaryOnlyMediator(context), context);

        var withSheets = await service.BuildAsync(Eleve2, Trimestre1, default);
        withSheets.Absences.Should().Be(1, "une absence non justifiée le dimanche 20");
        withSheets.Retards.Should().Be(0);
        withSheets.TotalAbsences.Should().Be(1);

        var withoutSheets = await service.BuildAsync(Eleve2, Trimestre2, default);
        withoutSheets.Absences.Should().BeNull("aucun appel n'a été fait ce trimestre : « - », jamais un zéro");
        withoutSheets.Retards.Should().BeNull();
        withoutSheets.TotalAbsences.Should().BeNull();
    }

    private static void AddSheet(
        ApplicationDbContext owner, DateOnly date, string period, params (Guid StudentId, AttendanceStatus Status)[] lines)
    {
        var sheet = new AttendanceSheet
        {
            SchoolId = Ecole, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee,
            Date = date, Period = period, TakenByUserId = Directeur.UserId!.Value
        };
        owner.AttendanceSheets.Add(sheet);

        foreach (var (studentId, status) in lines)
        {
            owner.StudentAttendances.Add(new StudentAttendance
            {
                SchoolId = Ecole, AttendanceSheetId = sheet.Id, StudentId = studentId, Status = status, LateMinutes = 0
            });
        }
    }
}
