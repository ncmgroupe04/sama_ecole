using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Application.SchoolYears.Commands.CreateSchoolYear;
using SamaEcole.Application.SchoolYears.Commands.RestoreSchoolYear;
using SamaEcole.Application.SchoolYears.Queries.GetDeletedSchoolYears;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.SoftDelete;

/// <summary>
/// Cycle de vie SchoolYear/Term (conception soft delete 2026-10-01 §3.2) : restauration en cascade des
/// trimestres et affectations archivés AVEC l'année, règle « une seule année active », chevauchement,
/// libellé repris, garde du rôle Directeur, isolation tenant et ARCHIVED_ENTITY_EXISTS à la création.
/// Les tombstones sont posés comme le fait l'archivage réel (DeleteSchoolYearCommandHandler, mode réel) :
/// année désactivée puis supprimée, trimestres et affectations supprimés ENSUITE.
/// </summary>
[Trait("Category", "MultiTenant")]
public class SchoolYearRestoreTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("61111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("62222222-2222-2222-2222-222222222222");
    private static readonly Guid Directeur = Guid.Parse("6d000000-0000-0000-0000-000000000001");

    private static readonly Guid AnneeArchivee = Guid.Parse("6a000000-0000-0000-0000-000000000001");
    private static readonly Guid TermeAvecAnnee1 = Guid.Parse("6e000000-0000-0000-0000-000000000001");
    private static readonly Guid TermeAvecAnnee2 = Guid.Parse("6e000000-0000-0000-0000-000000000002");
    private static readonly Guid TermeSupprimeAvant = Guid.Parse("6e000000-0000-0000-0000-000000000003");
    private static readonly Guid AffectationOk = Guid.Parse("6f000000-0000-0000-0000-000000000001");
    private static readonly Guid AffectationOrpheline = Guid.Parse("6f000000-0000-0000-0000-000000000002");

    private static readonly Guid Enseignant = Guid.Parse("60000000-0000-0000-0000-0000000000a1");
    private static readonly Guid ClasseVivante = Guid.Parse("60000000-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseSupprimee = Guid.Parse("60000000-0000-0000-0000-0000000000c2");
    private static readonly Guid Matiere = Guid.Parse("60000000-0000-0000-0000-0000000000b1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = EcoleA, Name = "École A" }, new School { Id = EcoleB, Name = "École B" });
        await owner.SaveChangesAsync(CancellationToken.None);

        var classeSupprimee = new Classroom { Id = ClasseSupprimee, SchoolId = EcoleA, Name = "5e B", Level = "5e", Capacity = 40, Cycle = CycleType.College };
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseVivante, SchoolId = EcoleA, Name = "6e A", Level = "6e", Capacity = 40, Cycle = CycleType.College },
            classeSupprimee);
        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = EcoleA, Name = "Histoire", Level = "Collège", Coefficient = 2 });
        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant, SchoolId = EcoleA, Matricule = "ENS-RY-001", FullName = "Moussa Diallo",
            Email = "moussa@example.test", BirthDate = new DateOnly(1988, 1, 1), BirthPlace = "Dakar", Status = EntityStatus.Active
        });

        var annee = new SchoolYear
        {
            Id = AnneeArchivee, SchoolId = EcoleA, Label = "2024-2025",
            StartDate = new DateOnly(2024, 10, 1), EndDate = new DateOnly(2025, 6, 30), IsActive = false
        };
        owner.SchoolYears.Add(annee);
        var t1 = new Term { Id = TermeAvecAnnee1, SchoolId = EcoleA, SchoolYearId = AnneeArchivee, Label = "T1", Order = 1, StartDate = new DateOnly(2024, 10, 1), EndDate = new DateOnly(2025, 1, 31) };
        var t2 = new Term { Id = TermeAvecAnnee2, SchoolId = EcoleA, SchoolYearId = AnneeArchivee, Label = "T2", Order = 2, StartDate = new DateOnly(2025, 2, 1), EndDate = new DateOnly(2025, 6, 30) };
        var t3 = new Term { Id = TermeSupprimeAvant, SchoolId = EcoleA, SchoolYearId = AnneeArchivee, Label = "T3", Order = 3, StartDate = new DateOnly(2025, 6, 1), EndDate = new DateOnly(2025, 6, 30) };
        owner.Terms.AddRange(t1, t2, t3);
        var ok = new TeacherAssignment { Id = AffectationOk, SchoolId = EcoleA, TeacherId = Enseignant, ClassroomId = ClasseVivante, SubjectId = Matiere, SchoolYearId = AnneeArchivee };
        var orpheline = new TeacherAssignment { Id = AffectationOrpheline, SchoolId = EcoleA, TeacherId = Enseignant, ClassroomId = ClasseSupprimee, SubjectId = Matiere, SchoolYearId = AnneeArchivee };
        owner.TeacherAssignments.AddRange(ok, orpheline);
        await owner.SaveChangesAsync(CancellationToken.None);

        // Ordre réel de l'archivage : un trimestre supprimé AVANT, puis l'année, puis ses enfants ;
        // la classe de l'affectation orpheline est supprimée plus tard encore.
        t3.SoftDelete("autre-raison");
        await owner.SaveChangesAsync(CancellationToken.None);
        await Task.Delay(30);
        annee.SoftDelete(Directeur.ToString());
        await owner.SaveChangesAsync(CancellationToken.None);
        await Task.Delay(30);
        t1.SoftDelete(Directeur.ToString());
        t2.SoftDelete(Directeur.ToString());
        ok.SoftDelete(Directeur.ToString());
        orpheline.SoftDelete(Directeur.ToString());
        await owner.SaveChangesAsync(CancellationToken.None);
        await Task.Delay(30);
        classeSupprimee.SoftDelete("test");
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<RestoreSchoolYearResult> RestoreAsync(Guid school, Guid id, Role role = Role.Directeur)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new RestoreSchoolYearCommandHandler(
                ctx, new FixedTenantProvider(school), new TestCurrentUser(Directeur, role), new NoOpKpiCache())
            .Handle(new RestoreSchoolYearCommand(id), CancellationToken.None);
    }

    private async Task<(bool Year, bool T1, bool T2, bool T3, bool Ok, bool Orphan, bool YearActive)> StateAsync()
    {
        await using var owner = _db.NewOwnerContext();
        var year = await owner.SchoolYears.IgnoreQueryFilters().SingleAsync(y => y.Id == AnneeArchivee);
        async Task<bool> Term(Guid id) => (await owner.Terms.IgnoreQueryFilters().SingleAsync(t => t.Id == id)).IsDeleted;
        async Task<bool> Assign(Guid id) => (await owner.TeacherAssignments.IgnoreQueryFilters().SingleAsync(a => a.Id == id)).IsDeleted;
        return (year.IsDeleted, await Term(TermeAvecAnnee1), await Term(TermeAvecAnnee2), await Term(TermeSupprimeAvant),
            await Assign(AffectationOk), await Assign(AffectationOrpheline), year.IsActive);
    }

    [Fact]
    public async Task Restoring_A_Year_Brings_Back_Only_The_Children_Archived_With_It()
    {
        var result = await RestoreAsync(EcoleA, AnneeArchivee);

        result.RestoredTerms.Should().Be(2);
        result.RestoredAssignments.Should().Be(1);
        result.SkippedAssignments.Should().Be(1, "la classe de l'affectation a été supprimée depuis");

        var s = await StateAsync();
        s.Year.Should().BeFalse();
        s.T1.Should().BeFalse();
        s.T2.Should().BeFalse();
        s.T3.Should().BeTrue("ce trimestre avait été supprimé avant l'année, pour une autre raison");
        s.Ok.Should().BeFalse();
        s.Orphan.Should().BeTrue("une affectation vers une classe supprimée n'est pas réactivée");
        s.YearActive.Should().BeFalse("la restauration n'active jamais l'année : l'activation reste explicite");

        await using var ctx = _db.NewAppContext(EcoleA);
        (await ctx.Terms.Where(t => t.SchoolYearId == AnneeArchivee).CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task Restored_Year_Leaves_The_Trash_And_Appears_In_The_Active_List()
    {
        await using (var ctx = _db.NewAppContext(EcoleA))
        {
            (await new GetDeletedSchoolYearsQueryHandler(ctx, new FixedTenantProvider(EcoleA))
                .Handle(new GetDeletedSchoolYearsQuery(), CancellationToken.None))
                .Should().ContainSingle(y => y.Id == AnneeArchivee);
        }

        await RestoreAsync(EcoleA, AnneeArchivee);

        await using var after = _db.NewAppContext(EcoleA);
        (await new GetDeletedSchoolYearsQueryHandler(after, new FixedTenantProvider(EcoleA))
            .Handle(new GetDeletedSchoolYearsQuery(), CancellationToken.None))
            .Should().NotContain(y => y.Id == AnneeArchivee);
        (await after.SchoolYears.AnyAsync(y => y.Id == AnneeArchivee)).Should().BeTrue();
    }

    [Fact]
    public async Task A_Tombstone_Still_Marked_Active_Cannot_Return_While_Another_Year_Is_Active()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            // Ligne antérieure à la désactivation à l'archivage : le tombstone porte encore IsActive = true.
            var legacy = await owner.SchoolYears.IgnoreQueryFilters().SingleAsync(y => y.Id == AnneeArchivee);
            legacy.IsActive = true;
            owner.SchoolYears.Add(new SchoolYear
            {
                SchoolId = EcoleA, Label = "2030-2031", StartDate = new DateOnly(2030, 10, 1),
                EndDate = new DateOnly(2031, 6, 30), IsActive = true
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = async () => await RestoreAsync(EcoleA, AnneeArchivee);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
        var s = await StateAsync();
        s.Year.Should().BeTrue("aucune mutation partielle");
        s.T1.Should().BeTrue();
        s.Ok.Should().BeTrue();
    }

    [Fact]
    public async Task A_Tombstone_Marked_Active_Returns_When_No_Other_Year_Is_Active()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            (await owner.SchoolYears.IgnoreQueryFilters().SingleAsync(y => y.Id == AnneeArchivee)).IsActive = true;
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await RestoreAsync(EcoleA, AnneeArchivee);

        (await StateAsync()).Year.Should().BeFalse();
    }

    [Fact]
    public async Task Restoring_Is_Refused_When_Another_Year_Took_The_Label()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolYears.Add(new SchoolYear
            {
                SchoolId = EcoleA, Label = "2024-2025", StartDate = new DateOnly(2040, 10, 1), EndDate = new DateOnly(2041, 6, 30)
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = async () => await RestoreAsync(EcoleA, AnneeArchivee);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
        (await StateAsync()).Year.Should().BeTrue();
    }

    [Fact]
    public async Task Restoring_Is_Refused_When_The_Period_Overlaps_A_Live_Year()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.SchoolYears.Add(new SchoolYear
            {
                SchoolId = EcoleA, Label = "2024-2025 bis", StartDate = new DateOnly(2025, 1, 1), EndDate = new DateOnly(2025, 12, 31)
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var act = async () => await RestoreAsync(EcoleA, AnneeArchivee);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ActiveEntityConflict);
        (await StateAsync()).Year.Should().BeTrue();
    }

    [Fact]
    public async Task Only_The_Directeur_Can_Restore_A_Year()
    {
        var act = async () => await RestoreAsync(EcoleA, AnneeArchivee, Role.Secretariat);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
        (await StateAsync()).Year.Should().BeTrue();
    }

    [Fact]
    public async Task Another_Tenant_Can_Neither_List_Nor_Restore_The_Archived_Year()
    {
        await using (var ctx = _db.NewAppContext(EcoleB))
        {
            (await new GetDeletedSchoolYearsQueryHandler(ctx, new FixedTenantProvider(EcoleB))
                .Handle(new GetDeletedSchoolYearsQuery(), CancellationToken.None))
                .Should().NotContain(y => y.Id == AnneeArchivee);
        }

        var act = async () => await RestoreAsync(EcoleB, AnneeArchivee);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        (await StateAsync()).Year.Should().BeTrue();
    }

    [Fact]
    public async Task Creating_A_Year_With_An_Archived_Label_Returns_Archived_Conflict_And_Inserts_Nothing()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new CreateSchoolYearCommandHandler(ctx, new FixedTenantProvider(EcoleA), TimeProvider.System);

        var act = async () => await handler.Handle(
            new CreateSchoolYearCommand
            {
                Label = "2024-2025", StartDate = new DateOnly(2050, 10, 1), EndDate = new DateOnly(2051, 6, 30)
            },
            CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>()).Which.Code.Should().Be(SoftDeleteLifecycle.ArchivedEntityExists);

        await using var owner = _db.NewOwnerContext();
        (await owner.SchoolYears.IgnoreQueryFilters().CountAsync(y => y.SchoolId == EcoleA && y.Label == "2024-2025")).Should().Be(1);
    }
}

file sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

file sealed class NoOpKpiCache : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}
