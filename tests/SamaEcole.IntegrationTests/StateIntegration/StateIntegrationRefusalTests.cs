using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.StateIntegration.Commands.AssignStudentIen;
using SamaEcole.Application.StateIntegration.Queries.GetPlaneteExport;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Files;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.StateIntegration;

/// <summary>
/// Refus explicites (<see cref="BusinessRuleException"/>, traduit en 409 par le middleware d'erreurs —
/// AGENTS.md règle #9) quand le code établissement national (SIMEN) manque — Volume 1 §23.1/§23.2.
/// Complète ACTIVE_CONTEXT.md (« Ce qui reste : ... refus 409 code absent »). Le comportement inverse
/// (succès une fois le code renseigné) est déjà couvert par SimenComplianceTests côté unitaire ;
/// ces tests-ci vérifient le REFUS contre une vraie base, RLS comprise.
/// </summary>
public class StateIntegrationRefusalTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleSansCode = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid AnneeA = Guid.Parse("aaaa2222-0000-0000-0000-000000000001");
    private static readonly Guid ClasseA = Guid.Parse("cccc2222-0000-0000-0000-000000000001");
    private static readonly Guid EleveA = Guid.Parse("eeee2222-0000-0000-0000-000000000001");

    private sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        // Établissement SANS NationalSchoolCode — c'est précisément le cas à tester.
        owner.Schools.Add(new School { Id = EcoleSansCode, Name = "École sans code SIMEN" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA, SchoolId = EcoleSansCode, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.Add(new Classroom
        {
            Id = ClasseA, SchoolId = EcoleSansCode, Name = "CM2 A", Level = "CM2",
            Cycle = CycleType.Primaire, Capacity = 40
        });
        owner.Students.Add(new Student
        {
            Id = EleveA, SchoolId = EcoleSansCode, Matricule = "ELEV-B-0001", FullName = "Fatou Ndiaye",
            BirthDate = new DateOnly(2016, 2, 10), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseA
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(EcoleSansCode);
    private static FixedTenantProvider Tenant() => new(EcoleSansCode);

    [Fact]
    public async Task Planete_Export_Is_Refused_Without_National_School_Code()
    {
        await using var ctx = Ctx();
        var handler = new GetPlaneteExportQueryHandler(
            ctx, Tenant(), new PlaneteExportSerializer(), TimeProvider.System);

        var act = async () => await handler.Handle(
            new GetPlaneteExportQuery(AnneeA, StateExportFormat.Csv), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>(
            "un export Planète sans code établissement serait rejeté, silencieusement et plus tard, par le SIMEN");
    }

    [Fact]
    public async Task Provisional_Ien_Generation_Is_Refused_Without_National_School_Code()
    {
        await using var ctx = Ctx();
        var handler = new AssignStudentIenCommandHandler(ctx, Tenant(), new NationalIenGenerator(
            ctx, _db.NewGenerator(ctx), TimeProvider.System));

        // IenNumber = null : demande de génération PROVISOIRE, pas d'attribution d'un numéro officiel.
        var act = async () => await handler.Handle(
            new AssignStudentIenCommand(EleveA, IenNumber: null), CancellationToken.None);

        await act.Should().ThrowAsync<BusinessRuleException>(
            "un IEN provisoire sans code établissement ne rattacherait l'élève à aucune école — "
            + "voir la mise en garde d'IIenGeneratorService");
    }

    [Fact]
    public async Task Refusal_Message_Points_To_Where_To_Fix_It()
    {
        await using var ctx = Ctx();
        var handler = new AssignStudentIenCommandHandler(ctx, Tenant(), new NationalIenGenerator(
            ctx, _db.NewGenerator(ctx), TimeProvider.System));

        var act = async () => await handler.Handle(
            new AssignStudentIenCommand(EleveA, IenNumber: null), CancellationToken.None);

        // Ticket ACTIVE_CONTEXT : la génération renvoyait un 500 nu avant correction. Le message doit
        // rester actionnable — pas une simple trace d'exception — pour l'agent au clavier.
        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Message.Should().Contain("Paramètres → Établissement");
    }
}
