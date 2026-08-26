using FluentAssertions;
using SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Finance;

/// <summary>
/// Ticket JGK-K01 — suggestion des heures de paie, rapprochée de l'emploi du temps. Purement
/// consultatif (Volume 1 §14.3 amendé) : ces tests portent sur l'agrégation et la détection d'écart,
/// jamais sur une écriture — la requête ne modifie jamais le contrat ni les enregistrements d'heures.
/// </summary>
[Trait("Category", "MultiTenant")]
public class GetSuggestedPayrollHoursQueryHandlerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-4444-1111-1111-111111111111");
    private static readonly Guid EnseignantA = Guid.Parse("eeeeeeee-4444-0000-0000-00000000000a");
    private static readonly Guid ContratVacataire = Guid.Parse("cccccccc-4444-0000-0000-00000000000a");
    private static readonly Guid ContratPersonnel = Guid.Parse("cccccccc-4444-0000-0000-00000000000b");
    private static readonly Guid UtilisateurPersonnel = Guid.Parse("aaaaaaaa-4444-0000-0000-00000000000a");
    private static readonly Guid ClasseA = Guid.Parse("ccccdddd-4444-0000-0000-00000000000a");
    private static readonly Guid MatiereA = Guid.Parse("ccccffff-4444-0000-0000-00000000000a");

    // Deux lundis du même mois : le créneau planifié (2h) est identique pour les deux, seules les
    // heures DÉCLARÉES diffèrent — ce qui isole proprement le cas "conforme" du cas "écart".
    private static readonly DateOnly PremierLundi = new(2026, 9, 7);
    private static readonly DateOnly SecondLundi = new(2026, 9, 14);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });

        owner.Teachers.Add(new Teacher
        {
            Id = EnseignantA, SchoolId = EcoleA, Matricule = "ENS-A-0001", FullName = "Cheikh Ndiaye",
            Email = "cheikh.ndiaye@ecole-a.sn", BirthDate = new DateOnly(1985, 1, 1)
        });

        owner.Users.Add(new User
        {
            Id = UtilisateurPersonnel, SchoolId = EcoleA, Email = "surveillant.a@ecole-a.sn", PasswordHash = "hash",
            FullName = "Surveillant A", Role = Role.Surveillant
        });

        owner.EmployeeContracts.AddRange(
            new EmployeeContract
            {
                Id = ContratVacataire, SchoolId = EcoleA, TeacherId = EnseignantA,
                Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 3_000m, TransportAllowance = 0m
            },
            new EmployeeContract
            {
                // Contrat lié à un UserId (personnel non-enseignant) : aucun ScheduleSlot possible.
                Id = ContratPersonnel, SchoolId = EcoleA, UserId = UtilisateurPersonnel,
                Type = ContractType.Vacataire, BaseSalary = 0m, HourlyRate = 2_000m, TransportAllowance = 0m
            });

        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.Add(new Subject { Id = MatiereA, SchoolId = EcoleA, Name = "Mathématiques", Level = "Collège" });

        // Créneau planifié : lundi 08h-10h, soit 2 heures — identique pour les deux lundis du test.
        owner.ScheduleSlots.Add(new ScheduleSlot
        {
            SchoolId = EcoleA, TeacherId = EnseignantA, ClassroomId = ClasseA, SubjectId = MatiereA,
            DayOfWeek = PremierLundi.DayOfWeek, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0)
        });

        owner.TeacherHourRecords.AddRange(
            // Conforme : 2h déclarées == 2h planifiées.
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratVacataire, Date = PremierLundi, Hours = 2m },
            // Écart : 3h déclarées != 2h planifiées.
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratVacataire, Date = SecondLundi, Hours = 3m },
            new TeacherHourRecord { SchoolId = EcoleA, EmployeeContractId = ContratPersonnel, Date = PremierLundi, Hours = 4m });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Suggested_Hours_Should_Sum_All_Declared_Hours_Of_The_Month()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.SuggestedHours.Should().Be(5m, "2h le premier lundi + 3h le second");
    }

    [Fact]
    public async Task A_Day_Matching_The_Schedule_Should_Not_Be_Reported_As_A_Discrepancy()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.Discrepancies.Should().NotContain(d => d.Date == PremierLundi);
    }

    [Fact]
    public async Task A_Day_Diverging_From_The_Schedule_Should_Be_Reported_But_Not_Block_Anything()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratVacataire, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        // Purement informatif (Volume 1 §14.3 amendé) : la requête renvoie l'écart, elle ne lève rien.
        result.Discrepancies.Should().ContainSingle(d => d.Date == SecondLundi)
            .Which.Should().BeEquivalentTo(new PayrollHoursDiscrepancyDto(SecondLundi, 3m, 2m));
    }

    [Fact]
    public async Task A_Contract_Linked_To_A_User_Should_Get_No_Discrepancy_Check()
    {
        // Personnel non-enseignant (UserId) : aucun ScheduleSlot n'existe pour lui, le rapprochement
        // n'a pas de sens — seule la suggestion agrégée est renvoyée.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var result = await handler.Handle(
            new GetSuggestedPayrollHoursQuery(ContratPersonnel, PremierLundi.Month, PremierLundi.Year),
            CancellationToken.None);

        result.SuggestedHours.Should().Be(4m);
        result.Discrepancies.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_Contract_Should_Throw_Not_Found()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetSuggestedPayrollHoursQueryHandler(ctx);

        var act = async () => await handler.Handle(
            new GetSuggestedPayrollHoursQuery(Guid.NewGuid(), 9, 2026), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
