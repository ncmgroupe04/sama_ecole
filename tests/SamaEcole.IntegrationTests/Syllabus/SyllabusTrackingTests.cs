using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.ClassJournal;
using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Syllabus;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Syllabus;

/// <summary>
/// Évolution N°7 — suivi du programme : import de la trame nationale, chapitres pointés au cahier de texte (contrôle du
/// programme de LA matière au niveau de LA classe), tableau d'avancement par classe, matière et enseignant, et tenue en
/// base des deux tables (RLS, aucun DELETE applicatif, purge par reset_school_data).
/// </summary>
[Trait("Category", "MultiTenant")]
public class SyllabusTrackingTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("71111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("72222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee = Guid.Parse("71111111-0000-0000-0000-000000000001");
    private static readonly Guid TroisiemeA = Guid.Parse("7ccccccc-0000-0000-0000-000000000001");
    private static readonly Guid TroisiemeB = Guid.Parse("7ccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Maths = Guid.Parse("7ddddddd-0000-0000-0000-000000000001");
    private static readonly Guid MathsAutreEcole = Guid.Parse("7ddddddd-0000-0000-0000-000000000002");

    private static readonly Guid CompteDiop = Guid.Parse("7fffffff-0000-0000-0000-0000000000f1");
    private static readonly Guid Diop = Guid.Parse("7fffffff-0000-0000-0000-0000000000f2");
    private static readonly Guid Sy = Guid.Parse("7fffffff-0000-0000-0000-0000000000f3");

    // Un lundi passé, dans l'année active : le créneau de M. Diop se cale sur son jour.
    private static readonly DateOnly Seance = new(2026, 9, 14);

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class CurrentUser(Guid? userId, Role? role) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => role;
        public string? IpAddress => "127.0.0.1";
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(new School { Id = Ecole, Name = "Collège A" }, new School { Id = AutreEcole, Name = "Collège B" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = TroisiemeA, SchoolId = Ecole, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 },
            new Classroom { Id = TroisiemeB, SchoolId = Ecole, Name = "3e B", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });
        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = MathsAutreEcole, SchoolId = AutreEcole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 });
        owner.Users.Add(new User
        {
            Id = CompteDiop, SchoolId = Ecole, Email = "diop@college-a.sn", PasswordHash = "hash-de-test", FullName = "Moussa Diop", Role = Role.Enseignant
        });
        owner.Teachers.AddRange(
            new Teacher { Id = Diop, SchoolId = Ecole, Matricule = "ENS-001", FullName = "Moussa Diop", Email = "diop@college-a.sn", BirthDate = new DateOnly(1985, 1, 1), UserId = CompteDiop },
            new Teacher { Id = Sy, SchoolId = Ecole, Matricule = "ENS-002", FullName = "Fatou Sy", Email = "sy@college-a.sn", BirthDate = new DateOnly(1988, 1, 1) });
        owner.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = Ecole, TeacherId = Diop, ClassroomId = TroisiemeA, SubjectId = Maths, SchoolYearId = Annee
        });
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { SchoolId = Ecole, TeacherId = Diop, ClassroomId = TroisiemeA, SubjectId = Maths, DayOfWeek = Seance.DayOfWeek, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
            new ScheduleSlot { SchoolId = Ecole, TeacherId = Sy, ClassroomId = TroisiemeB, SubjectId = Maths, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) });
        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(Ecole);

    private async Task<int> ImportMathsTroisiemeAsync()
    {
        await using var ctx = Ctx();
        return await new ImportSyllabusTemplateCommandHandler(ctx, new FixedTenantProvider(Ecole))
            .Handle(new ImportSyllabusTemplateCommand(Maths, "Troisième"), CancellationToken.None);
    }

    private async Task<List<Guid>> UnitIdsAsync(string grade = "Troisième")
    {
        await using var ctx = Ctx();
        return await ctx.SyllabusUnits.Where(u => u.SubjectId == Maths && u.GradeLevel == grade)
            .OrderBy(u => u.Order).Select(u => u.Id).ToListAsync();
    }

    private async Task<ClassJournalEntryResult> JournalAsync(IReadOnlyList<Guid>? unitIds)
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, new FixedTenantProvider(Ecole),
            new ClassJournalScopeAuthorizer(ctx, new CurrentUser(CompteDiop, Role.Enseignant)));
        return await handler.Handle(new CreateClassJournalEntryCommand
        {
            ClassroomId = TroisiemeA, SubjectId = Maths, SessionDate = Seance,
            Topic = "Racine carrée", Content = "Définition, propriétés, exercices.", SyllabusUnitIds = unitIds
        }, CancellationToken.None);
    }

    private async Task<SyllabusCoverageDto> CoverageAsync()
    {
        await using var ctx = Ctx();
        return await new GetSyllabusCoverageQueryHandler(ctx).Handle(new GetSyllabusCoverageQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task The_National_Template_Is_Imported_Once()
    {
        var first = await ImportMathsTroisiemeAsync();
        var second = await ImportMathsTroisiemeAsync();

        first.Should().Be(SyllabusTemplates.For("Mathématiques", "Troisième")!.Units.Count);
        second.Should().Be(0, "un chapitre déjà présent n'est jamais dupliqué");
    }

    [Fact]
    public async Task Bulk_Titles_Are_Appended_In_Order_Without_Duplicates()
    {
        await using (var ctx = Ctx())
        {
            var handler = new AddSyllabusUnitsCommandHandler(ctx, new FixedTenantProvider(Ecole));
            (await handler.Handle(new AddSyllabusUnitsCommand(Maths, "Sixième", "Géométrie", ["Droites", "Cercle"]), CancellationToken.None)).Should().Be(2);
        }

        await using (var ctx = Ctx())
        {
            var handler = new AddSyllabusUnitsCommandHandler(ctx, new FixedTenantProvider(Ecole));
            (await handler.Handle(new AddSyllabusUnitsCommand(Maths, "Sixième", null, ["cercle", "Angles"]), CancellationToken.None))
                .Should().Be(1, "« cercle » existe déjà, à la casse près");
        }

        await using var read = Ctx();
        var units = await new GetSyllabusUnitsQueryHandler(read).Handle(new GetSyllabusUnitsQuery(Maths, "Sixième", null), CancellationToken.None);
        units.Units.Select(u => u.Title).Should().Equal("Droites", "Cercle", "Angles");
        units.HasTemplate.Should().BeFalse("aucune trame nationale n'est codée pour les Mathématiques de Sixième");
    }

    [Fact]
    public async Task The_Programme_Of_A_Class_Follows_Its_Grade()
    {
        await ImportMathsTroisiemeAsync();

        await using var ctx = Ctx();
        var dto = await new GetSyllabusUnitsQueryHandler(ctx).Handle(new GetSyllabusUnitsQuery(Maths, null, TroisiemeA), CancellationToken.None);

        dto.GradeLevel.Should().Be("Troisième");
        dto.HasTemplate.Should().BeTrue();
        dto.Units.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Ticked_Units_Feed_The_Coverage_Per_Class_Subject_And_Teacher()
    {
        var total = await ImportMathsTroisiemeAsync();
        var ids = await UnitIdsAsync();

        await JournalAsync([ids[0], ids[1], ids[1]]);

        var coverage = await CoverageAsync();
        coverage.SchoolYearLabel.Should().Be("2026-2027");

        var a = coverage.Rows.Single(r => r.ClassroomId == TroisiemeA);
        a.CoveredUnits.Should().Be(2, "un chapitre coché deux fois compte une fois");
        a.TotalUnits.Should().Be(total);
        a.Percent.Should().Be(SyllabusCoverage.Percent(ids, [ids[0], ids[1]]));
        a.Teachers.Should().Equal("Moussa Diop");
        a.LastSessionDate.Should().Be(Seance);

        var b = coverage.Rows.Single(r => r.ClassroomId == TroisiemeB);
        b.CoveredUnits.Should().Be(0);
        b.Percent.Should().Be(0m);
        b.Teachers.Should().Equal(["Fatou Sy"], "l'enseignante de l'emploi du temps figure même sans séance au cahier");

        coverage.ByTeacher.Should().ContainSingle(t => t.TeacherName == "Fatou Sy" && t.AveragePercent == 0m);
        coverage.BySubject.Should().ContainSingle(s => s.SubjectName == "Mathématiques" && s.Classes == 2);
    }

    [Fact]
    public async Task A_Unit_Of_Another_Grade_Is_Refused()
    {
        await using (var ctx = Ctx())
        {
            await new AddSyllabusUnitsCommandHandler(ctx, new FixedTenantProvider(Ecole))
                .Handle(new AddSyllabusUnitsCommand(Maths, "Sixième", null, ["Nombres décimaux"]), CancellationToken.None);
        }

        var sixieme = await UnitIdsAsync("Sixième");

        var act = () => JournalAsync(sixieme);

        await act.Should().ThrowAsync<ValidationException>("la 3e A ne suit pas le programme de Sixième");

        await using var read = Ctx();
        (await read.ClassJournalEntries.CountAsync()).Should().Be(0, "la séance n'est pas enregistrée à moitié");
    }

    [Fact]
    public async Task Correcting_An_Entry_Replaces_Its_Units_And_Archives_The_Removed_Ones()
    {
        await ImportMathsTroisiemeAsync();
        var ids = await UnitIdsAsync();
        var created = await JournalAsync([ids[0], ids[1]]);

        await using (var ctx = Ctx())
        {
            var handler = new UpdateClassJournalEntryCommandHandler(ctx,
                new ClassJournalScopeAuthorizer(ctx, new CurrentUser(CompteDiop, Role.Enseignant)), TimeProvider.System,
                new CurrentUser(CompteDiop, Role.Enseignant));
            await handler.Handle(new UpdateClassJournalEntryCommand
            {
                Id = created.Id, Topic = "Racine carrée", Content = "Corrigé.", RowVersion = created.RowVersion,
                SyllabusUnitIds = [ids[1], ids[2]]
            }, CancellationToken.None);
        }

        await using var read = Ctx();
        (await read.ClassJournalEntryUnits.Where(l => l.ClassJournalEntryId == created.Id).Select(l => l.SyllabusUnitId).ToListAsync())
            .Should().BeEquivalentTo([ids[1], ids[2]]);
        (await read.ClassJournalEntryUnits.IgnoreQueryFilters().CountAsync(l => l.IsDeleted && l.SyllabusUnitId == ids[0]))
            .Should().Be(1, "le lien retiré est archivé, jamais supprimé (règle #6)");
    }

    [Fact]
    public async Task Archiving_A_Unit_With_A_Stale_RowVersion_Is_A_Conflict()
    {
        await ImportMathsTroisiemeAsync();
        await using var ctx = Ctx();
        var unit = await ctx.SyllabusUnits.AsNoTracking().FirstAsync();

        var handler = new DeleteSyllabusUnitCommandHandler(ctx, new CurrentUser(CompteDiop, Role.Directeur));
        var act = () => handler.Handle(new DeleteSyllabusUnitCommand(unit.Id, 1), CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    [Fact]
    public async Task Units_Of_Another_School_Are_Invisible_And_Unwritable()
    {
        await ImportMathsTroisiemeAsync();

        (await CountAsync(AutreEcole, "syllabus_units")).Should().Be(0);
        (await CountAsync(schoolId: null, "syllabus_units")).Should().Be(0, "sans tenant, rien n'est visible");

        var act = () => ExecuteAsync(Ecole, $"""
            INSERT INTO syllabus_units ("Id","SchoolId","SubjectId","GradeLevel","Title","Order","CreatedAt","IsDeleted")
            VALUES ('{Guid.NewGuid()}','{AutreEcole}','{MathsAutreEcole}','Troisième','Intrus',1,NOW(),FALSE);
            """);
        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }

    [Fact]
    public async Task The_Application_Role_Cannot_Physically_Delete_A_Link()
    {
        await ImportMathsTroisiemeAsync();
        var ids = await UnitIdsAsync();
        await JournalAsync([ids[0]]);

        var act = () => ExecuteAsync(Ecole, "DELETE FROM class_journal_entry_units;");

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "règle #6 : aucune suppression physique");
    }

    [Fact]
    public async Task Resetting_The_School_Data_Purges_Units_And_Links_Without_A_Foreign_Key_Error()
    {
        await ImportMathsTroisiemeAsync();
        var ids = await UnitIdsAsync();
        await JournalAsync([ids[0]]);

        await ExecuteAsync(Ecole, $"SELECT count(*) FROM reset_school_data('{Ecole}');");

        (await CountAsync(Ecole, "class_journal_entry_units")).Should().Be(0);
        (await CountAsync(Ecole, "syllabus_units")).Should().Be(0);
        (await CountAsync(Ecole, "class_journal_entries")).Should().Be(0);
    }

    private async Task ExecuteAsync(Guid schoolId, string sql)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> CountAsync(Guid? schoolId, string table)
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(schoolId);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""SELECT count(*) FROM "{table}" WHERE NOT "IsDeleted";""";
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
