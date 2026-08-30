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
/// Ticket JGK-T01 — retirer une matière qualifiée à un enseignant.
///
/// <c>UpdateTeacherCommandHandler</c> retire une qualification en supprimant PHYSIQUEMENT la ligne de
/// <c>teacher_subjects</c>. La migration <c>AddTeachers</c> n'accordait au rôle applicatif que
/// <c>SELECT, INSERT, UPDATE</c> : décocher une matière échouait donc en <c>42501: permission denied
/// for table teacher_subjects</c> (HTTP 500). <c>GrantDeleteOnTeacherSubjects</c> ajoute le
/// <c>DELETE</c> — ce test le prouve avec le rôle BRIDÉ, celui du runtime.
///
/// Deux vérifications, pas une : (1) l'opération n'échoue plus, et (2) la ligne a bien DISPARU en base
/// — sans quoi un grant absent se traduirait par un échec, mais un soft-delete silencieux (que rien
/// dans le code ne fait ici) passerait le test #1 tout en laissant une ligne fantôme.
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherSubjectUnassignmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Enseignant = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");
    private static readonly Guid MatiereGardee = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid MatiereRetiree = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2");

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
    public async Task Removing_A_Qualified_Subject_Deletes_The_Link_Without_A_Permission_Error()
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
            var handler = new UpdateTeacherCommandHandler(app);

            // On ne garde qu'UNE des deux matières : le Handler doit supprimer la ligne de l'autre.
            var act = async () => await handler.Handle(
                new UpdateTeacherCommand(
                    Enseignant, "Fatou Ndiaye", "fatou.ndiaye@baobabs.sn", null,
                    new DateOnly(1990, 4, 3), "Touba", null, null,
                    [MatiereGardee], rowVersion),
                CancellationToken.None);

            await act.Should().NotThrowAsync(
                "le rôle applicatif doit pouvoir supprimer une ligne de teacher_subjects (JGK-T01)");
        }

        // La ligne retirée a bien disparu — vérifié EN SQL BRUT avec le rôle applicatif, filtres EF
        // hors jeu. Une ligne soft-deletée (IsDeleted = true) ressortirait ici ; il ne doit rien y avoir.
        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();
        await using (var setTenant = connection.CreateCommand())
        {
            setTenant.CommandText = "SELECT set_config('app.current_school_id', @school, false)";
            setTenant.Parameters.AddWithValue("school", Ecole.ToString());
            await setTenant.ExecuteNonQueryAsync();
        }

        await using var count = connection.CreateCommand();
        count.CommandText =
            """
            SELECT count(*) FROM teacher_subjects
            WHERE "TeacherId" = @teacher AND "SubjectId" = @subject
            """;
        count.Parameters.AddWithValue("teacher", Enseignant);
        count.Parameters.AddWithValue("subject", MatiereRetiree);

        var remaining = (long)(await count.ExecuteScalarAsync())!;
        remaining.Should().Be(0, "la qualification retirée est supprimée physiquement, pas masquée");

        // La matière conservée, elle, est toujours là.
        await using var kept = connection.CreateCommand();
        kept.CommandText =
            """
            SELECT count(*) FROM teacher_subjects
            WHERE "TeacherId" = @teacher AND "SubjectId" = @subject
            """;
        kept.Parameters.AddWithValue("teacher", Enseignant);
        kept.Parameters.AddWithValue("subject", MatiereGardee);

        var stillThere = (long)(await kept.ExecuteScalarAsync())!;
        stillThere.Should().Be(1, "seule la matière décochée devait partir");
    }
}
