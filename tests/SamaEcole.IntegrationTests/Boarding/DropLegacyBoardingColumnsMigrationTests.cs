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
/// Migration <c>DropLegacyBoardingColumns</c> (lot F2) : supprime <c>enrollments.BoardingStatus</c> et <c>enrollments.RoomId</c> avec leur clé étrangère
/// composite et leur index. Le schéma COURANT est celui d'après la migration ; le schéma d'AVANT (colonnes présentes) s'obtient en reculant d'une
/// migration, exactement ce que fait un retour arrière en production. <c>Down()</c> restitue les colonnes en re-dérivant leur valeur des séjours actifs,
/// sans jamais violer la clé étrangère : une chambre créée par la nouvelle API n'a pas de ligne dans <c>rooms</c>, donc pas de <c>RoomId</c>.
/// </summary>
[Trait("Category", "MultiTenant")]
public class DropLegacyBoardingColumnsMigrationTests : LeaveTestBase
{
    /// <summary>Dernière migration d'avant le lot F2 : colonnes héritées présentes.</summary>
    private const string BeforeDrop = "20261007090741_ReconcileBoardingWithLegacyModel";

    /// <summary>
    /// PORTE DE SÉCURITÉ à passer (résultat 0) en préproduction ET en production avant de déployer la migration. Inscriptions de l'année active, non
    /// annulées, que l'ancien modèle déclare pensionnaires et qui n'ont AUCUN séjour — actif OU clos. Un résultat non nul signale soit une
    /// réconciliation manquée, soit une ANCIENNE INSTANCE qui écrit encore les colonnes héritées (elle ne crée pas de séjour).
    /// Un séjour clos depuis le lot C (fin de séjour, libération) n'est PAS un écart : la colonne héritée est figée, le séjour existe.
    /// Cette requête est reprise telle quelle dans le runbook de la PR F2.
    /// </summary>
    private const string GateSql = """
        SELECT count(*)
        FROM enrollments e
        JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
        WHERE e."BoardingStatus" <> 'Externe'
          AND e."Status" <> 'Cancelled'
          AND NOT e."IsDeleted"
          AND NOT EXISTS (SELECT 1 FROM boarding_enrollments be WHERE be."SchoolId" = e."SchoolId" AND be."EnrollmentId" = e."Id")
        """;

    // ------------------------------------------------------------------ Outils

    private async Task MigrateToAsync(string? target)
    {
        await using var owner = Db.NewOwnerContext();
        await owner.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<T> ScalarAsync<T>(string sql, params (string Name, object? Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(Db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default! : (T)result;
    }

    private async Task ExecAsync(string sql, params (string Name, object? Value)[] parameters) =>
        await ScalarAsync<object>(sql, parameters);

    private async Task<(string? Status, Guid? Room)> LegacyColumnsOfAsync(string key)
    {
        await using var connection = new NpgsqlConnection(Db.OwnerConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT e."BoardingStatus", e."RoomId" FROM enrollments e
            JOIN boarding_enrollments be ON be."SchoolId" = e."SchoolId" AND be."EnrollmentId" = e."Id"
            WHERE be."Id" = @stay
            """, connection);
        command.Parameters.AddWithValue("stay", Stays[key]);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue();
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetGuid(1));
    }

    private async Task<bool> ColumnExistsAsync(string column) =>
        await ScalarAsync<bool>(
            """SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name = 'enrollments' AND column_name = @c)""", ("c", column));

    // ------------------------------------------------------------------ Up

    [Fact]
    public async Task Up_Removes_The_Two_Columns_Their_Foreign_Key_And_Their_Index()
    {
        // Le schéma courant est celui d'après la migration.
        (await ColumnExistsAsync("BoardingStatus")).Should().BeFalse();
        (await ColumnExistsAsync("RoomId")).Should().BeFalse();

        (await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_enrollments_rooms_SchoolId_RoomId')")).Should().BeFalse();
        (await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_enrollments_SchoolId_RoomId')")).Should().BeFalse();
    }

    [Fact]
    public async Task Up_Keeps_The_Stays_And_Every_Other_Enrollment_Column_Intact()
    {
        var before = await ScalarAsync<long>("SELECT count(*) FROM boarding_enrollments");
        before.Should().BeGreaterThan(0);

        await MigrateToAsync(BeforeDrop);
        await MigrateToAsync(null);

        (await ScalarAsync<long>("SELECT count(*) FROM boarding_enrollments")).Should().Be(before);
        (await ScalarAsync<long>("SELECT count(*) FROM enrollments WHERE \"ReceiptNumber\" LIKE 'REC-%'")).Should().BeGreaterThan(0);
        (await ColumnExistsAsync("BoardingStatus")).Should().BeFalse();
    }

    // ------------------------------------------------------------------ Down

    [Fact]
    public async Task Down_Restores_The_Columns_With_Their_Original_Definition()
    {
        await MigrateToAsync(BeforeDrop);

        (await ColumnExistsAsync("BoardingStatus")).Should().BeTrue();
        (await ColumnExistsAsync("RoomId")).Should().BeTrue();
        (await ScalarAsync<string>(
            """SELECT is_nullable FROM information_schema.columns WHERE table_name = 'enrollments' AND column_name = 'BoardingStatus'""")).Should().Be("NO");
        (await ScalarAsync<int>(
            """SELECT character_maximum_length FROM information_schema.columns WHERE table_name = 'enrollments' AND column_name = 'BoardingStatus'""")).Should().Be(20);
        (await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conname = 'FK_enrollments_rooms_SchoolId_RoomId')")).Should().BeTrue();
        (await ScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'IX_enrollments_SchoolId_RoomId')")).Should().BeTrue();
    }

    [Fact]
    public async Task Down_Re_Derives_The_Legacy_Values_From_The_Active_Stays_Without_Breaking_The_Foreign_Key()
    {
        // « Chambre 102 » existe aussi dans l'ancien modèle (une ligne rooms de même Id : cas d'une chambre reprise au lot A).
        // « Chambre 101 » n'existe QUE dans le nouveau modèle (créée par la nouvelle API) : aucune ligne rooms, donc pas de RoomId.
        var chambre102 = await ScalarAsync<Guid>("""SELECT "Id" FROM dormitory_rooms WHERE "Name" = 'Chambre 102' AND "SchoolId" = @s""", ("s", EcoleA));
        await using (var owner = Db.NewOwnerContext())
        {
            var building = new Building { Id = Guid.NewGuid(), SchoolId = EcoleA, Name = "Ancien bâtiment" };
            owner.Buildings.Add(building);
            owner.Rooms.Add(new Room { Id = chambre102, SchoolId = EcoleA, BuildingId = building.Id, Name = "Dortoir 102", Capacity = 2, Type = RoomType.Dortoir });
            await owner.SaveChangesAsync();
        }

        await MigrateToAsync(BeforeDrop);

        (await LegacyColumnsOfAsync("m3")).Should().Be(("Interne", (Guid?)chambre102), "séjour actif sur un lit d'une chambre qui existe dans l'ancien modèle");
        (await LegacyColumnsOfAsync("m1")).Should().Be(("Interne", (Guid?)null), "chambre du nouveau modèle seulement : aucune ligne rooms, donc pas de RoomId");
        (await LegacyColumnsOfAsync("attente")).Should().Be(("Interne", (Guid?)null), "interne en attente de lit : régime sans chambre");
        (await LegacyColumnsOfAsync("demi")).Should().Be(("DemiPensionnaire", (Guid?)null), "un demi-pensionnaire n'a jamais de chambre");
        (await LegacyColumnsOfAsync("clos")).Should().Be(("Externe", (Guid?)null), "séjour clos : l'élève n'est plus pensionnaire");
        (await LegacyColumnsOfAsync("autre")).Should().Be(("Interne", (Guid?)null), "école B : même règle, jamais de chambre d'une autre école");
    }

    [Fact]
    public async Task Down_Then_Up_Can_Be_Replayed_With_The_Same_Result()
    {
        await MigrateToAsync(BeforeDrop);
        await MigrateToAsync(null);
        await MigrateToAsync(BeforeDrop);
        await MigrateToAsync(null);

        (await ColumnExistsAsync("BoardingStatus")).Should().BeFalse();
        (await ScalarAsync<long>("SELECT count(*) FROM boarding_enrollments WHERE \"IsActive\"")).Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------ Porte de sécurité (schéma d'avant la migration)

    /// <summary>Pose les colonnes héritées d'une inscription (schéma d'avant la migration : elles existent).</summary>
    private Task SetLegacyAsync(string key, string status) =>
        ExecAsync(
            """
            UPDATE enrollments SET "BoardingStatus" = @status
            WHERE "Id" = (SELECT "EnrollmentId" FROM boarding_enrollments WHERE "Id" = @stay)
            """, ("status", status), ("stay", Stays[key]));

    [Fact]
    public async Task The_Gate_Is_Zero_When_Every_Legacy_Boarder_Has_A_Stay_Even_An_Ended_One()
    {
        await MigrateToAsync(BeforeDrop);
        await SetLegacyAsync("m1", "Interne");
        await SetLegacyAsync("demi", "DemiPensionnaire");
        await SetLegacyAsync("clos", "Interne"); // séjour CLOS depuis : la colonne héritée est figée, ce n'est pas un écart

        (await ScalarAsync<long>(GateSql)).Should().Be(0);
    }

    [Fact]
    public async Task The_Gate_Counts_A_Legacy_Boarder_Who_Has_No_Stay_At_All()
    {
        await MigrateToAsync(BeforeDrop);
        var orphan = await AddEnrollmentWithoutStayAsync("orphelin", AnneeA, EnrollmentStatus.Confirmed);
        await ExecAsync("""UPDATE enrollments SET "BoardingStatus" = 'Interne' WHERE "Id" = @id""", ("id", orphan));

        (await ScalarAsync<long>(GateSql)).Should().Be(1, "une ancienne instance a écrit le régime sans créer de séjour");
    }

    [Fact]
    public async Task The_Gate_Ignores_Cancelled_Enrollments_And_Those_Of_Other_Years()
    {
        await MigrateToAsync(BeforeDrop);
        var cancelled = await AddEnrollmentWithoutStayAsync("annulee", AnneeA, EnrollmentStatus.Cancelled);
        var past = await AddEnrollmentWithoutStayAsync("passee", AnneeAPassee, EnrollmentStatus.Confirmed);
        await ExecAsync("""UPDATE enrollments SET "BoardingStatus" = 'Interne' WHERE "Id" IN (@a, @b)""", ("a", cancelled), ("b", past));

        (await ScalarAsync<long>(GateSql)).Should().Be(0);
    }

    private async Task<Guid> AddEnrollmentWithoutStayAsync(string key, Guid year, EnrollmentStatus status)
    {
        await using var owner = Db.NewOwnerContext();
        var classroom = await owner.Classrooms.IgnoreQueryFilters().FirstAsync(c => c.SchoolId == EcoleA);
        var student = new Student
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, Matricule = $"M-{key}", FullName = $"Élève {key}",
            BirthDate = new DateOnly(2014, 1, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = classroom.Id
        };
        var enrollment = new Enrollment
        {
            Id = Guid.NewGuid(), SchoolId = EcoleA, StudentId = student.Id, SchoolYearId = year, ClassroomId = classroom.Id,
            Type = EnrollmentType.NewEnrollment, Status = status, TotalDue = 15_000m, ReceiptNumber = $"REC-{key}", EnrolledAt = DateTimeOffset.UtcNow
        };
        owner.Students.Add(student);
        owner.Enrollments.Add(enrollment);
        await owner.SaveChangesAsync();
        return enrollment.Id;
    }
}
