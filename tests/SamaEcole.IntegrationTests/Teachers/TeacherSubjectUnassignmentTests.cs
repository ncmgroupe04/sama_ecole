using FluentAssertions;
using SamaEcole.Application.Teachers.Commands.UpdateTeacher;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Teachers;

/// <summary>
/// Une qualification retirée reste une trace historique tenant-scoped : le parcours actif la masque,
/// tandis que les colonnes tombstone portent l'acteur et la date de la révocation.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherSubjectUnassignmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Enseignant = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid MatiereGardee = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid MatiereRetiree = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2");
    private static readonly Guid Directeur = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Subjects.AddRange(
            new Subject { Id = MatiereGardee, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = MatiereRetiree, SchoolId = Ecole, Name = "Physique & Chimie", Level = "Collège", Coefficient = 3 });
        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant,
            SchoolId = Ecole,
            Matricule = "ENS-2026-0001",
            FullName = "Fatou Ndiaye",
            Email = "fatou.ndiaye@baobabs.sn",
            BirthDate = new DateOnly(1990, 4, 3),
            BirthPlace = "Touba",
            Status = EntityStatus.Active
        });
        owner.TeacherSubjects.AddRange(
            new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = MatiereGardee },
            new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = MatiereRetiree });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Removing_A_Qualified_Subject_Retains_A_Revoked_Link_With_Its_Audit_Fields()
    {
        // Jeton xmin lu tel que le ferait la fiche avant modification.
        uint rowVersion;
        await using (var read = _db.NewAppContext(Ecole))
        {
            rowVersion = await read.Teachers
                .Where(t => t.Id == Enseignant)
                .Select(t => EF.Property<uint>(t, "xmin"))
                .SingleAsync();
        }

        await using (var app = _db.NewAppContext(Ecole))
        {
            var handler = new UpdateTeacherCommandHandler(app, new TestCurrentUser(Directeur));

            // On ne garde qu'UNE des deux matières : le Handler doit révoquer la ligne de l'autre.
            var act = async () => await handler.Handle(
                new UpdateTeacherCommand(
                    Enseignant, "Fatou Ndiaye", "fatou.ndiaye@baobabs.sn", null,
                    new DateOnly(1990, 4, 3), "Touba", null, null,
                    [MatiereGardee], rowVersion),
                CancellationToken.None);

            await act.Should().NotThrowAsync(
                "la révocation est un UPDATE autorisé au rôle applicatif, pas un DELETE");
        }

        // SQL brut sous le rôle applicatif : le tombstone existe encore même si le filtre EF l'exclut
        // des sélecteurs actifs.
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = connection.CreateCommand())
        {
            setTenant.CommandText = "SELECT set_config('app.current_school_id', @school, false)";
            setTenant.Parameters.AddWithValue("school", Ecole.ToString());
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var revoked = connection.CreateCommand();
        revoked.CommandText =
            """
            SELECT "IsDeleted", "DeletedAt", "DeletedBy" FROM teacher_subjects
            WHERE "TeacherId" = @teacher AND "SubjectId" = @subject
            """;
        revoked.Parameters.AddWithValue("teacher", Enseignant);
        revoked.Parameters.AddWithValue("subject", MatiereRetiree);

        await using var reader = await revoked.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("la qualification révoquée est conservée pour l'historique");
        reader.GetBoolean(0).Should().BeTrue();
        reader.IsDBNull(1).Should().BeFalse("la date de révocation est obligatoire");
        reader.GetString(2).Should().Be(Directeur.ToString(), "l'acteur de la révocation est conservé");
        await reader.DisposeAsync();

        await using (var active = _db.NewAppContext(Ecole))
        {
            (await active.TeacherSubjects.AnyAsync(ts => ts.TeacherId == Enseignant && ts.SubjectId == MatiereRetiree))
                .Should().BeFalse("une qualification révoquée est absente des parcours actifs");
        }

        // La matière conservée, elle, reste active.
        await using var kept = connection.CreateCommand();
        kept.CommandText =
            """
            SELECT count(*) FROM teacher_subjects
            WHERE "TeacherId" = @teacher AND "SubjectId" = @subject
            """;
        kept.Parameters.AddWithValue("teacher", Enseignant);
        kept.Parameters.AddWithValue("subject", MatiereGardee);

        var stillThere = (long)(await kept.ExecuteScalarAsync())!;
        stillThere.Should().Be(1, "seule la matière décochée devait être révoquée");
    }

    [Fact]
    public async Task Partial_Index_Allows_Several_Tombstones_But_Only_One_Active_Link()
    {
        await using var owner = _db.NewOwnerContext();
        await owner.Database.ExecuteSqlRawAsync(
            "UPDATE teacher_subjects SET \"IsDeleted\" = true, \"DeletedAt\" = now(), \"DeletedBy\" = 'test' " +
            $"WHERE \"TeacherId\" = '{Enseignant}' AND \"SubjectId\" = '{MatiereRetiree}'");

        // Nouvelle qualification active après révocation, révoquée à son tour, puis une troisième.
        owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = MatiereRetiree });
        await owner.SaveChangesAsync(CancellationToken.None);
        await owner.Database.ExecuteSqlRawAsync(
            "UPDATE teacher_subjects SET \"IsDeleted\" = true, \"DeletedAt\" = now(), \"DeletedBy\" = 'test' " +
            $"WHERE \"TeacherId\" = '{Enseignant}' AND \"SubjectId\" = '{MatiereRetiree}' AND NOT \"IsDeleted\"");
        owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = MatiereRetiree });
        await owner.SaveChangesAsync(CancellationToken.None);

        // Un second lien actif pour la même paire reste refusé.
        owner.TeacherSubjects.Add(new TeacherSubject { SchoolId = Ecole, TeacherId = Enseignant, SubjectId = MatiereRetiree });
        var act = async () => await owner.SaveChangesAsync(CancellationToken.None);
        await act.Should().ThrowAsync<SamaEcole.Application.Common.Exceptions.DuplicateRecordException>();
    }

    [Fact]
    public async Task App_Role_Cannot_Physically_Delete_A_Teacher_Subject()
    {
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = connection.CreateCommand())
        {
            setTenant.CommandText = "SELECT set_config('app.current_school_id', @school, false)";
            setTenant.Parameters.AddWithValue("school", Ecole.ToString());
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM teacher_subjects WHERE \"TeacherId\" = @teacher AND \"SubjectId\" = @subject";
        delete.Parameters.AddWithValue("teacher", Enseignant);
        delete.Parameters.AddWithValue("subject", MatiereRetiree);

        var act = async () => await delete.ExecuteNonQueryAsync();
        var failure = await act.Should().ThrowAsync<PostgresException>();
        failure.Which.SqlState.Should().Be(PostgresErrorCodes.InsufficientPrivilege);
    }
}
