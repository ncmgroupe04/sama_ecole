using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.IntegrationTests.Boarding;

/// <summary>
/// Migration <c>AddAttendanceSessions</c> sur PostgreSQL réel : reprise des pointages existants dans une session « Appel de nuit »,
/// retour arrière, déclencheur de compatibilité (ancienne révision pendant un déploiement) et garde-fous posés par la base (RLS, CHECK,
/// unicité).
/// </summary>
[Trait("Category", "MultiTenant")]
public class AttendanceSessionsMigrationTests : LeaveTestBase
{
    private const string PreviousMigration = "20261013015740_AddHizbEvaluationJournal";

    private BoardingSqlSeed Sql => new(Db);

    private async Task MigrateToAsync(string? target)
    {
        await using var owner = Db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var owner = Db.NewOwnerContext();
        var connection = owner.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<bool> SessionsTableExistsAsync() =>
        ScalarAsync<bool>("SELECT to_regclass('public.attendance_sessions') IS NOT NULL");

    private async Task OldInsertAsync(Guid school, string stayKey, string date, bool deleted = false)
    {
        // Ancien schéma : ni SessionId ni StudentId.
        await using var owner = Db.NewOwnerContext();
        await owner.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO boarding_attendances ("Id","SchoolId","BoardingEnrollmentId","Date","IsPresent","CreatedAt","IsDeleted")
            VALUES (gen_random_uuid(), {0}, {1}, {2}::date, TRUE, NOW(), {3})
            """, school, Stays[stayKey], date, deleted);
    }

    [Fact]
    public async Task The_Migration_Moves_Existing_Rolls_Into_A_Night_Session_Per_School_And_Can_Be_Reverted()
    {
        await MigrateToAsync(PreviousMigration);
        (await SessionsTableExistsAsync()).Should().BeFalse("la migration est reculée");

        await OldInsertAsync(EcoleA, "m1", "2026-10-02");
        await OldInsertAsync(EcoleA, "m1", "2026-10-03");
        await OldInsertAsync(EcoleA, "m2", "2026-10-02");
        await OldInsertAsync(EcoleA, "m2", "2026-10-04", deleted: true);   // une ligne supprimée logiquement est reprise aussi
        await OldInsertAsync(EcoleB, "autre", "2026-10-02");

        await MigrateToAsync(null);

        await using (var owner = Db.NewOwnerContext())
        {
            var sessions = await owner.AttendanceSessions.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            sessions.Should().HaveCount(2, "une session par école ayant déjà des pointages");
            sessions.Should().OnlyContain(s => s.Name == "Appel de nuit" && s.Audience == AttendanceAudience.Interns
                && s.Days == AttendanceDays.All && s.IsActive && !s.IsDeleted);

            var rows = await owner.BoardingAttendances.IgnoreQueryFilters().AsNoTracking().ToListAsync();
            rows.Should().HaveCount(5);
            rows.Where(r => r.SchoolId == EcoleA).Should().OnlyContain(r => r.SessionId == sessions.Single(s => s.SchoolId == EcoleA).Id);
            rows.Where(r => r.SchoolId == EcoleB).Should().OnlyContain(r => r.SessionId == sessions.Single(s => s.SchoolId == EcoleB).Id);

            var students = await owner.BoardingEnrollments.IgnoreQueryFilters().AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.StudentId);
            rows.Should().OnlyContain(r => r.StudentId == students[r.BoardingEnrollmentId!.Value], "l'élève est déduit du séjour");
        }

        // Retour arrière : des pointages hors « Appel de nuit » (autre session, appel de Halqa) ne tiennent pas dans l'ancien modèle.
        await using (var owner = Db.NewOwnerContext())
        {
            var night = await owner.AttendanceSessions.IgnoreQueryFilters().FirstAsync(s => s.SchoolId == EcoleA);
            var lunch = new AttendanceSession { SchoolId = EcoleA, Name = "Déjeuner", Audience = AttendanceAudience.AllBoarders, SortOrder = 1 };
            owner.AttendanceSessions.Add(lunch);
            var student = (await owner.BoardingEnrollments.IgnoreQueryFilters().FirstAsync(b => b.Id == Stays["m3"])).StudentId;
            owner.BoardingAttendances.AddRange(
                new BoardingAttendance { SchoolId = EcoleA, SessionId = lunch.Id, StudentId = student, BoardingEnrollmentId = Stays["m3"], Date = new DateOnly(2026, 10, 2), IsPresent = true },
                new BoardingAttendance { SchoolId = EcoleA, SessionId = night.Id, StudentId = student, BoardingEnrollmentId = null, Date = new DateOnly(2026, 10, 2), IsPresent = true });
            await owner.SaveChangesAsync();
        }

        await MigrateToAsync(PreviousMigration);

        (await SessionsTableExistsAsync()).Should().BeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM boarding_attendances")).Should().Be(5, "seuls les appels de nuit d'un séjour survivent");
        (await ScalarAsync<string>("""SELECT is_nullable FROM information_schema.columns WHERE table_name = 'boarding_attendances' AND column_name = 'BoardingEnrollmentId'"""))
            .Should().Be("NO", "l'ancien modèle exige le séjour");
        (await ScalarAsync<bool>("SELECT to_regclass('public.\"UX_boarding_attendances_night\"') IS NOT NULL")).Should().BeTrue("l'unicité (séjour, nuit) est restaurée");
        (await ScalarAsync<string>("SELECT pg_get_functiondef('reset_school_data(uuid)'::regprocedure)")).Should().NotContain("attendance_sessions");

        // Et de nouveau en avant : la reprise se refait sans erreur.
        await MigrateToAsync(null);
        (await SessionsTableExistsAsync()).Should().BeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM attendance_sessions")).Should().Be(2);
        (await ScalarAsync<string>("SELECT pg_get_functiondef('reset_school_data(uuid)'::regprocedure)")).Should().Contain("attendance_sessions");
    }

    [Fact]
    public async Task A_School_Without_Any_Roll_Gets_No_Session_From_The_Migration()
    {
        await MigrateToAsync(PreviousMigration);
        await OldInsertAsync(EcoleA, "m1", "2026-10-02");

        await MigrateToAsync(null);

        (await ScalarAsync<long>($"""SELECT count(*) FROM attendance_sessions WHERE "SchoolId" = '{EcoleB}'""")).Should().Be(0, "aucune session n'est imposée");
        (await ScalarAsync<long>($"""SELECT count(*) FROM attendance_sessions WHERE "SchoolId" = '{EcoleA}'""")).Should().Be(1);
    }

    [Fact]
    public async Task An_Insert_Without_Student_Or_Session_Like_The_Old_Revision_Does_Is_Completed_By_The_Database()
    {
        // L'ancienne révision écrit encore (BoardingEnrollmentId, Date, IsPresent) pendant un déploiement progressif.
        var first = await Sql.InsertAttendanceAsync(EcoleA, EcoleA, Stays["m1"], "2026-10-02");

        await using var owner = Db.NewOwnerContext();
        var row = await owner.BoardingAttendances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == first);
        var student = (await owner.BoardingEnrollments.IgnoreQueryFilters().AsNoTracking().SingleAsync(b => b.Id == Stays["m1"])).StudentId;
        row.StudentId.Should().Be(student);
        var session = await owner.AttendanceSessions.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == row.SessionId);
        session.Should().Match<AttendanceSession>(s => s.SchoolId == EcoleA && s.Name == "Appel de nuit" && s.Audience == AttendanceAudience.Interns);

        // La même session sert la nuit suivante, et une seconde saisie de la même nuit reste refusée (unicité).
        var second = await Sql.InsertAttendanceAsync(EcoleA, EcoleA, Stays["m1"], "2026-10-03");
        (await owner.BoardingAttendances.IgnoreQueryFilters().AsNoTracking().SingleAsync(a => a.Id == second)).SessionId.Should().Be(session.Id);

        var duplicate = async () => await Sql.InsertAttendanceAsync(EcoleA, EcoleA, Stays["m1"], "2026-10-02");
        await duplicate.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task The_Compatibility_Trigger_Prefers_An_Existing_Intern_Session_Over_Creating_One()
    {
        await Sql.ExecuteAsync(EcoleA, """
            INSERT INTO attendance_sessions ("Id","SchoolId","Name","Audience","Days","IsActive","SortOrder","CreatedAt","IsDeleted")
            VALUES (gen_random_uuid(), @school, 'Veillée', 'Interns', 127, TRUE, 0, NOW(), FALSE)
            """, ("school", EcoleA));

        var id = await Sql.InsertAttendanceAsync(EcoleA, EcoleA, Stays["m1"], "2026-10-02");

        (await ScalarAsync<long>($"""SELECT count(*) FROM attendance_sessions WHERE "SchoolId" = '{EcoleA}'""")).Should().Be(1, "aucune session créée en plus");
        (await ScalarAsync<string>($"""SELECT s."Name" FROM attendance_sessions s JOIN boarding_attendances a ON a."SessionId" = s."Id" WHERE a."Id" = '{id}'"""))
            .Should().Be("Veillée");
    }

    [Fact]
    public async Task The_Database_Isolates_Sessions_By_School_And_Guards_Names_Days_And_Physical_Deletes()
    {
        const string insert = """
            INSERT INTO attendance_sessions ("Id","SchoolId","Name","Audience","Days","IsActive","SortOrder","CreatedAt","IsDeleted")
            VALUES (gen_random_uuid(), @school, @name, @audience, @days, TRUE, 0, NOW(), FALSE)
            """;

        await Sql.ExecuteAsync(EcoleA, insert, ("school", EcoleA), ("name", "Déjeuner"), ("audience", "AllBoarders"), ("days", 127));
        await Sql.ExecuteAsync(EcoleB, insert, ("school", EcoleB), ("name", "Déjeuner"), ("audience", "AllBoarders"), ("days", 127));

        (await Sql.CountAsync(EcoleA, "attendance_sessions")).Should().Be(1, "chaque école ne voit que ses sessions");
        (await Sql.CountAsync(EcoleB, "attendance_sessions")).Should().Be(1);

        var foreign = async () => await Sql.ExecuteAsync(EcoleA, insert, ("school", EcoleB), ("name", "Piraté"), ("audience", "Halqa"), ("days", 127));
        await foreign.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "la RLS refuse d'écrire pour une autre école");

        var duplicateName = async () => await Sql.ExecuteAsync(EcoleA, insert, ("school", EcoleA), ("name", "Déjeuner"), ("audience", "Halqa"), ("days", 127));
        await duplicateName.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.UniqueViolation);

        foreach (var days in new[] { 0, 128 })
        {
            var badDays = async () => await Sql.ExecuteAsync(EcoleA, insert, ("school", EcoleA), ("name", $"Jours {days}"), ("audience", "Halqa"), ("days", days));
            await badDays.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.CheckViolation, $"Days = {days}");
        }

        var delete = async () => await Sql.ExecuteAsync(EcoleA, "DELETE FROM attendance_sessions");
        await delete.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege, "suppression logique seulement");

        // Une session supprimée logiquement libère son nom.
        await Sql.ExecuteAsync(EcoleA, """UPDATE attendance_sessions SET "IsDeleted" = TRUE WHERE "Name" = 'Déjeuner'""");
        await Sql.ExecuteAsync(EcoleA, insert, ("school", EcoleA), ("name", "Déjeuner"), ("audience", "Halqa"), ("days", 127));
    }
}
