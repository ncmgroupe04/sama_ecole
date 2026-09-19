using FluentAssertions;
using SamaEcole.Application.ClassJournal;
using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using SamaEcole.Application.ClassJournal.Commands.DeleteClassJournalEntry;
using SamaEcole.Application.ClassJournal.Commands.UpdateClassJournalEntry;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.ClassJournal;

/// <summary>
/// Ticket JGK-P04 — la garde d'écriture du cahier de texte repose sur DEUX remontées de base
/// (compte → fiche enseignant via Teacher.UserId, puis fiche → TeacherAssignment/ScheduleSlot),
/// donc testée en intégration et non unitairement — même raison qu'ExamDossierScopeTests.
/// </summary>
public class ClassJournalScopeTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid Classe = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClasseNonAssignee = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Matiere = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private static readonly Guid CompteProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f1");
    private static readonly Guid FicheProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f2");
    private static readonly Guid CompteDirecteur = Guid.Parse("ffffffff-0000-0000-0000-0000000000f3");

    // Un lundi fixe, choisi une fois : le ScheduleSlot planifié se cale sur SON DayOfWeek, pour ne
    // jamais dépendre d'une coïncidence de calendrier entre les deux valeurs.
    private static readonly DateOnly SeanceTenue = new(2026, 9, 14);

    private sealed class FixedTenantProvider(Guid schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    private sealed class CurrentUser(Guid? userId, Role? role) : ICurrentUserService
    {
        public Guid? UserId => userId;
        public Role? Role => role;
        public string? IpAddress => "127.0.0.1";

        public static CurrentUser Enseignant(Guid userId) => new(userId, SamaEcole.Domain.Enums.Role.Enseignant);
        public static CurrentUser Directeur(Guid userId) => new(userId, SamaEcole.Domain.Enums.Role.Directeur);
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = Ecole, Name = "École A" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });

        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseNonAssignee, SchoolId = Ecole, Name = "CM2 B", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });

        owner.Users.AddRange(
            new User { Id = CompteProf, SchoolId = Ecole, Email = "prof@ecole-a.sn", PasswordHash = "hash-de-test", FullName = "Awa Fall", Role = Role.Enseignant },
            new User { Id = CompteDirecteur, SchoolId = Ecole, Email = "directeur@ecole-a.sn", PasswordHash = "hash-de-test", FullName = "Directeur A", Role = Role.Directeur });

        owner.Teachers.Add(new Teacher
        {
            Id = FicheProf, SchoolId = Ecole, Matricule = "ENS-2026-001", FullName = "Awa Fall",
            Email = "prof@ecole-a.sn", BirthDate = new DateOnly(1990, 4, 3), UserId = CompteProf
        });

        // Affectation active SUR LA SEULE classe Classe (pas ClasseNonAssignee).
        owner.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = Ecole, TeacherId = FicheProf, ClassroomId = Classe, SubjectId = Matiere, SchoolYearId = Annee
        });

        // Créneau hebdomadaire planifié le jour de SeanceTenue, et lui seul.
        owner.ScheduleSlots.Add(new ScheduleSlot
        {
            SchoolId = Ecole, TeacherId = FicheProf, ClassroomId = Classe, SubjectId = Matiere,
            DayOfWeek = SeanceTenue.DayOfWeek, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(9, 0)
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(Ecole);
    private static FixedTenantProvider Tenant() => new(Ecole);

    private static CreateClassJournalEntryCommand NewCommand(Guid classroomId, DateOnly? sessionDate = null) => new()
    {
        ClassroomId = classroomId,
        SubjectId = Matiere,
        SessionDate = sessionDate ?? SeanceTenue,
        Topic = "Le théorème de Pythagore",
        Content = "Démonstration au tableau, exercices 1 à 4 du manuel."
    };

    [Fact]
    public async Task Enseignant_Titulaire_Du_Creneau_Journalise_Sa_Seance()
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)));

        var result = await handler.Handle(NewCommand(Classe), CancellationToken.None);

        result.TeacherId.Should().Be(FicheProf, "l'auteur vient du compte courant, jamais de la requête");
    }

    [Fact]
    public async Task Enseignant_Sans_Affectation_Sur_La_Classe_Est_Refuse_En_409()
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)));

        var act = async () => await handler.Handle(NewCommand(ClasseNonAssignee), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("SCHEDULE_SLOT_NOT_PLANNED");
    }

    [Fact]
    public async Task Enseignant_Affecte_Mais_Sans_Creneau_Ce_Jour_La_Est_Refuse_En_409()
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)));

        // Affecté à Classe, mais AUCUN ScheduleSlot n'existe pour le jour suivant.
        var jourSansCreneau = SeanceTenue.AddDays(1);

        var act = async () => await handler.Handle(NewCommand(Classe, jourSansCreneau), CancellationToken.None);

        (await act.Should().ThrowAsync<BusinessRuleException>())
            .Which.Code.Should().Be("SCHEDULE_SLOT_NOT_PLANNED");
    }

    [Fact]
    public async Task Compte_Enseignant_Sans_Fiche_Est_Refuse_Explicitement()
    {
        await using var ctx = Ctx();
        var orphelin = CurrentUser.Enseignant(Guid.Parse("dddddddd-0000-0000-0000-0000000000d9"));
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, orphelin));

        var act = async () => await handler.Handle(NewCommand(Classe), CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Contain("fiche enseignant");
    }

    [Fact]
    public async Task Directeur_Ne_Peut_Pas_Journaliser_A_La_Place_De_Lenseignant()
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Directeur(CompteDirecteur)));

        var act = async () => await handler.Handle(NewCommand(Classe), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "seul l'Enseignant crée une entrée — le contrôleur le borne déjà, l'autorisateur le revérifie indépendamment");
    }

    // ---------------------------------------------------------------- Correction (15 jours)

    private async Task<(Guid Id, uint RowVersion)> CreerEntreeAsync(DateOnly sessionDate)
    {
        await using var ctx = Ctx();
        var handler = new CreateClassJournalEntryCommandHandler(ctx, Tenant(), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)));
        var result = await handler.Handle(NewCommand(Classe, sessionDate), CancellationToken.None);
        return (result.Id, result.RowVersion);
    }

    [Fact]
    public async Task Auteur_Corrige_Sa_Propre_Entree_Dans_Les_15_Jours()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var (id, rowVersion) = await CreerEntreeAsync(today.DayOfWeek == SeanceTenue.DayOfWeek ? today : SeanceTenue);

        await using var ctx = Ctx();
        var handler = new UpdateClassJournalEntryCommandHandler(
            ctx, new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)), TimeProvider.System);

        var result = await handler.Handle(
            new UpdateClassJournalEntryCommand { Id = id, Topic = "Sujet corrigé", Content = "Contenu corrigé", RowVersion = rowVersion },
            CancellationToken.None);

        result.Topic.Should().Be("Sujet corrigé");
    }

    [Fact]
    public async Task Auteur_Ne_Peut_Plus_Corriger_Apres_15_Jours()
    {
        var (id, rowVersion) = await CreerEntreeAsync(SeanceTenue);

        await using var ctx = Ctx();
        // Horloge figée à J+16 après la séance : le délai est dépassé.
        var horlogeApresDelai = new FakeTimeProvider(SeanceTenue.ToDateTime(TimeOnly.MinValue).AddDays(16));
        var handler = new UpdateClassJournalEntryCommandHandler(
            ctx, new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)), horlogeApresDelai);

        var act = async () => await handler.Handle(
            new UpdateClassJournalEntryCommand { Id = id, Topic = "Tentative tardive", Content = "…", RowVersion = rowVersion },
            CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Contain("15 jours");
    }

    [Fact]
    public async Task Directeur_Corrige_Une_Entree_Meme_Apres_15_Jours()
    {
        var (id, rowVersion) = await CreerEntreeAsync(SeanceTenue);

        await using var ctx = Ctx();
        var horlogeApresDelai = new FakeTimeProvider(SeanceTenue.ToDateTime(TimeOnly.MinValue).AddDays(40));
        var handler = new UpdateClassJournalEntryCommandHandler(
            ctx, new ClassJournalScopeAuthorizer(ctx, CurrentUser.Directeur(CompteDirecteur)), horlogeApresDelai);

        var result = await handler.Handle(
            new UpdateClassJournalEntryCommand { Id = id, Topic = "Correction du Directeur", Content = "…", RowVersion = rowVersion },
            CancellationToken.None);

        result.Topic.Should().Be("Correction du Directeur");
    }

    [Fact]
    public async Task Un_Autre_Enseignant_Ne_Peut_Pas_Corriger_Lentree_Dun_Collegue()
    {
        var (id, rowVersion) = await CreerEntreeAsync(SeanceTenue);

        await using var owner = _db.NewOwnerContext();
        var compteAutreProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f4");
        var ficheAutreProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f5");
        owner.Users.Add(new User { Id = compteAutreProf, SchoolId = Ecole, Email = "autre@ecole-a.sn", PasswordHash = "hash-de-test", FullName = "Autre Prof", Role = Role.Enseignant });
        owner.Teachers.Add(new Teacher { Id = ficheAutreProf, SchoolId = Ecole, Matricule = "ENS-2026-002", FullName = "Autre Prof", Email = "autre@ecole-a.sn", BirthDate = new DateOnly(1988, 1, 1), UserId = compteAutreProf });
        await owner.SaveChangesAsync(CancellationToken.None);

        await using var ctx = Ctx();
        var handler = new UpdateClassJournalEntryCommandHandler(
            ctx, new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(compteAutreProf)), TimeProvider.System);

        var act = async () => await handler.Handle(
            new UpdateClassJournalEntryCommand { Id = id, Topic = "Tentative d'un collègue", Content = "…", RowVersion = rowVersion },
            CancellationToken.None);

        (await act.Should().ThrowAsync<ForbiddenException>())
            .Which.Message.Should().Contain("propres entrées");
    }

    [Fact]
    public async Task Suppression_Suit_La_Meme_Garde_Des_15_Jours()
    {
        var (id, rowVersion) = await CreerEntreeAsync(SeanceTenue);

        await using var ctx = Ctx();
        var horlogeApresDelai = new FakeTimeProvider(SeanceTenue.ToDateTime(TimeOnly.MinValue).AddDays(20));
        var handler = new DeleteClassJournalEntryCommandHandler(
            ctx, CurrentUser.Enseignant(CompteProf), new ClassJournalScopeAuthorizer(ctx, CurrentUser.Enseignant(CompteProf)), horlogeApresDelai);

        var act = async () => await handler.Handle(new DeleteClassJournalEntryCommand(id, rowVersion), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    private sealed class FakeTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(DateTime.SpecifyKind(utcNow, DateTimeKind.Utc));
    }
}
