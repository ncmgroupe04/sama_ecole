using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Teachers.Commands.AssignTeacher;
using SamaEcole.Application.Teachers.Commands.UnassignTeacher;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Teachers;

/// <summary>
/// Cycle de vie d'une attribution enseignant : attribuer → retirer → RÉATTRIBUER.
///
/// Le troisième temps est le seul qui compte vraiment ici. Le retrait est une suppression LOGIQUE
/// (AGENTS.md règle #6) : la ligne reste en base avec <c>IsDeleted = true</c>. Or l'index unique
/// (TeacherId, ClassroomId, SubjectId, SchoolYearId) ne connaît pas cette colonne — pour PostgreSQL,
/// la place reste donc occupée par une attribution que l'utilisateur croit avoir supprimée, et
/// l'établissement ne peut plus jamais rendre cette matière à cet enseignant dans cette classe pour
/// l'année en cours. Un index unique posé sur une table à soft delete DOIT être partiel ; le dépôt
/// le fait déjà ailleurs (AddSchoolYears, AddEnrollments, AddSurveillantEntities).
/// </summary>
[Trait("Category", "MultiTenant")]
public class TeacherAssignmentLifecycleTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Enseignant = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Classe = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid Matiere = Guid.Parse("eeeeeeee-0000-0000-0000-00000000000e");
    private static readonly Guid Annee = Guid.Parse("11111111-0000-0000-0000-000000000001");

    private static readonly Guid Acteur = Guid.Parse("99999999-0000-0000-0000-000000000009");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.Classrooms.Add(new Classroom
        {
            Id = Classe, SchoolId = Ecole, Name = "3 eme", Level = "Collège", Capacity = 40
        });
        owner.Subjects.Add(new Subject
        {
            Id = Matiere, SchoolId = Ecole, Name = "Physique & Chimie", Level = "Collège", Coefficient = 3
        });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2025-2026",
            StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 6, 30), IsActive = true
        });
        owner.Teachers.Add(new Teacher
        {
            Id = Enseignant,
            SchoolId = Ecole,
            Matricule = "ENS-2026-0001",
            FullName = "Fatou Ndiaye",
            Email = "fatou.ndiaye@baobabs.sn",
            BirthDate = new DateOnly(1995, 4, 3),
            BirthPlace = "Touba",
            Status = EntityStatus.Active
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Reassigning_After_A_Removal_Should_Be_Allowed()
    {
        Guid assignmentId;

        // 1. Attribution initiale.
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var result = await NewAssignHandler(ctx).Handle(
                new AssignTeacherCommand { TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere },
                CancellationToken.None);
            assignmentId = result.Id;
        }

        // 2. Retrait par le Directeur (suppression logique).
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await NewUnassignHandler(ctx).Handle(
                new UnassignTeacherCommand(Enseignant, assignmentId), CancellationToken.None);
        }

        // La fiche ne doit plus montrer l'attribution retirée.
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var visibles = await ctx.TeacherAssignments.CountAsync(a => a.TeacherId == Enseignant);
            visibles.Should().Be(0, "le retrait masque l'attribution dans la fiche enseignant");
        }

        // 3. Le Directeur se ravise et rend la matière au même enseignant, dans la même classe.
        await using (var reassign = _db.NewAppContext(Ecole))
        {
            var act = async () => await NewAssignHandler(reassign).Handle(
                new AssignTeacherCommand { TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere },
                CancellationToken.None);

            await act.Should().NotThrowAsync(
                "une attribution retirée libère la place : la refuser enferme l'établissement dans une " +
                "impasse dont aucun écran ne permet de sortir");
        }

        await using (var verif = _db.NewAppContext(Ecole))
        {
            var visibles = await verif.TeacherAssignments.CountAsync(a => a.TeacherId == Enseignant);
            visibles.Should().Be(1, "la réattribution rétablit exactement une ligne visible");
        }
    }

    [Fact]
    public async Task Assigning_The_Same_Pair_Twice_Should_Be_Refused_With_A_Readable_Message()
    {
        await using (var ctx = _db.NewAppContext(Ecole))
        {
            await NewAssignHandler(ctx).Handle(
                new AssignTeacherCommand { TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere },
                CancellationToken.None);
        }

        await using (var ctx = _db.NewAppContext(Ecole))
        {
            var act = async () => await NewAssignHandler(ctx).Handle(
                new AssignTeacherCommand { TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere },
                CancellationToken.None);

            // BusinessRuleException → 409, et non ValidationException → 422. Un doublon d'attribution
            // est un CONFLIT d'état — la ressource existe déjà — pas une saisie mal formée : les deux
            // champs envoyés sont valides, c'est la combinaison qui est déjà prise.
            //
            // C'est aussi le contrat PUBLIÉ (openapi.yaml : « 409 : Cette attribution (enseignant,
            // classe, matière, année) existe déjà »), la convention du dépôt pour un doublon
            // (Volume_4_API_Design.md §392) et le code que rendait l'index unique avant que le
            // pré-contrôle n'existe. Ce pré-contrôle améliore le MESSAGE, il ne change pas le CODE —
            // sans quoi il constituerait une rupture de contrat silencieuse pour les clients.
            var thrown = await act.Should().ThrowAsync<BusinessRuleException>(
                "un doublon est un conflit d'état, refusé en 409 comme le publie openapi.yaml");

            // Le message part vers un directeur ou un secrétariat, pas vers un développeur : il ne doit
            // porter ni nom de table, ni nom d'index, ni vocabulaire de base de données.
            thrown.Which.Message.Should().NotContainAny(
                ["teacher_assignments", "IX_", "entité", "index", "contrainte", "SQL"]);
        }
    }

    /// <summary>Conflit d'écriture RÉEL : deux insertions simultanées de la même attribution.</summary>
    [Fact]
    public async Task Two_Simultaneous_Assignments_Should_Not_Create_A_Duplicate()
    {
        await using var ctxA = _db.NewAppContext(Ecole);
        await using var ctxB = _db.NewAppContext(Ecole);

        // Les deux contextes ont lu la fiche AVANT que l'autre n'écrive : le pré-contrôle du Handler
        // passe des deux côtés, seule la base peut encore trancher.
        ctxA.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = Ecole, TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee
        });
        await ctxA.SaveChangesAsync(CancellationToken.None);

        ctxB.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = Ecole, TeacherId = Enseignant, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee
        });
        var act = async () => await ctxB.SaveChangesAsync(CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<ConcurrencyConflictException>(
            "la base reste le dernier rempart contre le doublon (AGENTS.md règle #5)");

        thrown.Which.Message.Should().NotContainAny(
            ["teacher_assignments", "IX_", "SQL"],
            "le message remonte tel quel jusqu'à l'écran : il ne doit jamais nommer une table ni un index");
    }

    private static AssignTeacherCommandHandler NewAssignHandler(IApplicationDbContext ctx)
        => new(ctx, new FixedTenant(Ecole));

    private static UnassignTeacherCommandHandler NewUnassignHandler(IApplicationDbContext ctx)
        => new(ctx, new FixedTenant(Ecole), new FixedUser(Acteur));

    private sealed class FixedTenant(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class FixedUser(Guid userId) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => Domain.Enums.Role.Directeur;
        public string? IpAddress => "127.0.0.1";
    }
}
