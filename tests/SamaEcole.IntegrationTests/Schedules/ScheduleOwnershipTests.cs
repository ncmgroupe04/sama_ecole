using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.Schools;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Schedules;

/// <summary>
/// Contrôle de propriété des créneaux d'emploi du temps (AGENTS.md règle #10, Volume 1 §21.2).
///
/// Un Enseignant ne crée, ne modifie et ne supprime un créneau que pour LUI-MÊME. Les trois verbes
/// sont testés : le verrou posé à la seule création se contournerait en deux appels (créer pour soi,
/// puis réattribuer à un collègue), et interdire la modification en laissant la suppression ouverte
/// ne protégerait rien.
///
/// Test d'intégration et non unitaire : la règle repose sur la remontée compte → fiche
/// (`Teacher.UserId`), donc sur une vraie requête, pas sur de la logique en mémoire.
/// </summary>
public class ScheduleOwnershipTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Classe = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid Matiere = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");

    private static readonly Guid CompteAwa = Guid.Parse("dddddddd-0000-0000-0000-0000000000d1");
    private static readonly Guid FicheAwa = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e1");
    private static readonly Guid FicheModou = Guid.Parse("eeeeeeee-0000-0000-0000-0000000000e2");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        // Jeu de données posé par le PROPRIÉTAIRE (exempté de RLS) : deux enseignants de la MÊME
        // école — l'isolation inter-écoles n'est pas le sujet ici, la propriété intra-école l'est.
        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });

        owner.Classrooms.Add(new Classroom
        {
            Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Capacity = 40
        });

        owner.Subjects.Add(new Subject
        {
            Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4
        });

        // Le COMPTE de connexion d'Awa doit exister : Teacher.UserId porte une clé étrangère vers
        // users. C'est ce lien compte → fiche qui est le cœur du contrôle testé ici.
        owner.Users.Add(new User
        {
            Id = CompteAwa, SchoolId = Ecole, Email = "awa@ecole-a.sn",
            PasswordHash = "hash-de-test", FullName = "Awa Fall", Role = Role.Enseignant
        });

        owner.Teachers.AddRange(
            new Teacher
            {
                Id = FicheAwa, SchoolId = Ecole, Matricule = "ENS-2026-001", FullName = "Awa Fall",
                Email = "awa@ecole-a.sn", BirthDate = new DateOnly(1990, 4, 3), UserId = CompteAwa
            },
            new Teacher
            {
                Id = FicheModou, SchoolId = Ecole, Matricule = "ENS-2026-002", FullName = "Modou Diop",
                Email = "modou@ecole-a.sn", BirthDate = new DateOnly(1988, 11, 20), UserId = null
            });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Enseignant_Cree_Un_Creneau_Pour_Lui_Meme()
    {
        await using var context = _db.NewAppContext(Ecole);

        var id = await CreateAsync(context, CurrentUser.Enseignant(CompteAwa), FicheAwa);

        var slot = await context.ScheduleSlots.FindAsync(id);
        slot!.TeacherId.Should().Be(FicheAwa);
        slot.IsTeacherSubmitted.Should().BeTrue("un créneau proposé par un enseignant reste distinguable");
    }

    [Fact]
    public async Task Enseignant_Ne_Cree_Pas_Un_Creneau_Au_Nom_D_Un_Collegue()
    {
        await using var context = _db.NewAppContext(Ecole);

        var act = async () => await CreateAsync(context, CurrentUser.Enseignant(CompteAwa), FicheModou);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Directeur_Cree_Un_Creneau_Pour_N_Importe_Quel_Enseignant()
    {
        await using var context = _db.NewAppContext(Ecole);

        var id = await CreateAsync(context, CurrentUser.Directeur(), FicheModou);

        var slot = await context.ScheduleSlots.FindAsync(id);
        slot!.TeacherId.Should().Be(FicheModou);
        slot.IsTeacherSubmitted.Should().BeFalse();
    }

    [Fact]
    public async Task Enseignant_Ne_Reattribue_Pas_Son_Creneau_A_Un_Collegue()
    {
        await using var context = _db.NewAppContext(Ecole);

        // Le créneau appartient bien à Awa : c'est la RÉATTRIBUTION qui doit être refusée, sans quoi
        // le verrou de la création se contournerait en deux appels.
        var id = await CreateAsync(context, CurrentUser.Enseignant(CompteAwa), FicheAwa);

        var act = async () => await UpdateAsync(context, CurrentUser.Enseignant(CompteAwa), id, FicheModou);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Enseignant_Ne_Modifie_Pas_Le_Creneau_D_Un_Collegue()
    {
        await using var context = _db.NewAppContext(Ecole);

        var id = await CreateAsync(context, CurrentUser.Directeur(), FicheModou);

        // Awa tente de s'approprier le créneau de Modou.
        var act = async () => await UpdateAsync(context, CurrentUser.Enseignant(CompteAwa), id, FicheAwa);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Enseignant_Ne_Supprime_Pas_Le_Creneau_D_Un_Collegue()
    {
        await using var context = _db.NewAppContext(Ecole);

        var id = await CreateAsync(context, CurrentUser.Directeur(), FicheModou);

        var user = CurrentUser.Enseignant(CompteAwa);
        var handler = new DeleteScheduleSlotCommandHandler(context, user, Authorizer(context, user));

        var act = async () => await handler.Handle(new DeleteScheduleSlotCommand(id), default);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Enseignant_Supprime_Son_Propre_Creneau()
    {
        await using var context = _db.NewAppContext(Ecole);

        var id = await CreateAsync(context, CurrentUser.Enseignant(CompteAwa), FicheAwa);

        var user = CurrentUser.Enseignant(CompteAwa);
        var handler = new DeleteScheduleSlotCommandHandler(context, user, Authorizer(context, user));
        await handler.Handle(new DeleteScheduleSlotCommand(id), default);

        // Contexte NEUF : FindAsync sur le contexte d'origine rendrait l'entité encore suivie par le
        // ChangeTracker sans passer par le filtre, et le test passerait au vert sans rien prouver.
        // Suppression LOGIQUE (règle #6) : la ligne existe toujours en base, le filtre la masque.
        await using var relecture = _db.NewAppContext(Ecole);
        (await relecture.ScheduleSlots.FindAsync(id)).Should().BeNull();
    }

    [Fact]
    public async Task Compte_Enseignant_Sans_Fiche_Est_Refuse_Explicitement()
    {
        await using var context = _db.NewAppContext(Ecole);

        // Compte porteur du rôle Enseignant, mais qu'aucune fiche ne référence : on ne peut pas savoir
        // de qui serait le créneau. Refus, et message qui indique l'action corrective.
        var orphelin = CurrentUser.Enseignant(Guid.Parse("dddddddd-0000-0000-0000-0000000000d9"));

        var act = async () => await CreateAsync(context, orphelin, FicheAwa);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .Which.Message.Should().Contain("fiche enseignant");
    }

    private static ScheduleOwnershipAuthorizer Authorizer(IApplicationDbContext context, ICurrentUserService user)
        => new(context, user);

    private static async Task<Guid> CreateAsync(
        SamaEcole.Persistence.ApplicationDbContext context, ICurrentUserService user, Guid teacherId)
    {
        var handler = new CreateScheduleSlotCommandHandler(
            context, new StubTenantProvider(Ecole), user, Authorizer(context, user), new WorkingDayGuard(context));

        return await handler.Handle(
            new CreateScheduleSlotCommand(
                teacherId, Classe, Matiere, DayOfWeek.Monday,
                new TimeOnly(8, 0), new TimeOnly(10, 0), "Salle 1"),
            default);
    }

    private static async Task UpdateAsync(
        SamaEcole.Persistence.ApplicationDbContext context, ICurrentUserService user, Guid id, Guid teacherId)
    {
        var handler = new UpdateScheduleSlotCommandHandler(context, user, Authorizer(context, user), new WorkingDayGuard(context));

        await handler.Handle(
            new UpdateScheduleSlotCommand(
                id, teacherId, Classe, Matiere, DayOfWeek.Tuesday,
                new TimeOnly(8, 0), new TimeOnly(10, 0), "Salle 2"),
            default);
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class CurrentUser(Guid? userId, Role? role) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => role;
        public string? IpAddress => "127.0.0.1";

        public static CurrentUser Enseignant(Guid userId) => new(userId, SamaEcole.Domain.Enums.Role.Enseignant);
        public static CurrentUser Directeur() => new(Guid.NewGuid(), SamaEcole.Domain.Enums.Role.Directeur);
    }
}
