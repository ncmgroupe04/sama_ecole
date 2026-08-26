using FluentAssertions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams;
using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Application.Exams.Queries.GetExamDossierDetail;
using SamaEcole.Application.Exams.Queries.GetExamDossiers;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// Portée de lecture des dossiers d'examen ouverte à l'Enseignant (ticket JGK-J08, Volume 4 §22) :
/// un dossier porte des données d'état civil sensibles, donc un Enseignant ne voit que les dossiers
/// des classes où il a une affectation ACTIVE. Test d'intégration et non unitaire : la règle repose
/// sur la remontée compte → fiche (Teacher.UserId) puis fiche → classe (TeacherAssignments), donc sur
/// une vraie requête — même raison que <see cref="Schedules.ScheduleOwnershipTests"/>.
/// </summary>
public class ExamDossierScopeTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Annee = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid ClasseAssignee = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClasseNonAssignee = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid Matiere = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");
    private static readonly Guid EleveA = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid EleveB = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid Session = Guid.Parse("55550001-0000-0000-0000-000000000001");

    private static readonly Guid CompteProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f1");
    private static readonly Guid FicheProf = Guid.Parse("ffffffff-0000-0000-0000-0000000000f2");

    private Guid _dossierAssigne;
    private Guid _dossierNonAssigne;

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
        public static CurrentUser Directeur() => new(Guid.NewGuid(), SamaEcole.Domain.Enums.Role.Directeur);
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
            new Classroom { Id = ClasseAssignee, SchoolId = Ecole, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseNonAssignee, SchoolId = Ecole, Name = "CM2 B", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 });

        owner.Subjects.Add(new Subject { Id = Matiere, SchoolId = Ecole, Name = "Mathématiques", Level = "Primaire", Coefficient = 4 });

        owner.Students.AddRange(
            new Student { Id = EleveA, SchoolId = Ecole, Matricule = "ELEV-A-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseAssignee },
            new Student { Id = EleveB, SchoolId = Ecole, Matricule = "ELEV-A-0002", FullName = "Modou Diop", BirthDate = new DateOnly(2015, 5, 2), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseNonAssignee });

        owner.ExamSessions.Add(new ExamSession { Id = Session, SchoolId = Ecole, SchoolYearId = Annee, ExamType = ExamType.CFEE });

        // Le compte de connexion du professeur : Teacher.UserId porte une clé étrangère vers users.
        owner.Users.Add(new User
        {
            Id = CompteProf, SchoolId = Ecole, Email = "prof@ecole-a.sn",
            PasswordHash = "hash-de-test", FullName = "Awa Fall", Role = Role.Enseignant
        });
        owner.Teachers.Add(new Teacher
        {
            Id = FicheProf, SchoolId = Ecole, Matricule = "ENS-2026-001", FullName = "Awa Fall",
            Email = "prof@ecole-a.sn", BirthDate = new DateOnly(1990, 4, 3), UserId = CompteProf
        });

        // Affectation active du professeur SUR UNE SEULE des deux classes.
        owner.TeacherAssignments.Add(new TeacherAssignment
        {
            SchoolId = Ecole, TeacherId = FicheProf, ClassroomId = ClasseAssignee, SubjectId = Matiere, SchoolYearId = Annee
        });

        await owner.SaveChangesAsync(CancellationToken.None);

        // Un dossier par classe, ouvert par un compte non borné (Directeur).
        await using var ctx = Ctx();
        var tenant = Tenant();
        var handler = new CreateExamDossierCommandHandler(ctx, tenant);

        _dossierAssigne = (await handler.Handle(
            new CreateExamDossierCommand { ExamSessionId = Session, StudentId = EleveA, ClassroomId = ClasseAssignee },
            CancellationToken.None)).Id;

        await using var ctx2 = Ctx();
        var handler2 = new CreateExamDossierCommandHandler(ctx2, tenant);
        _dossierNonAssigne = (await handler2.Handle(
            new CreateExamDossierCommand { ExamSessionId = Session, StudentId = EleveB, ClassroomId = ClasseNonAssignee },
            CancellationToken.None)).Id;
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private ApplicationDbContext Ctx() => _db.NewAppContext(Ecole);
    private static FixedTenantProvider Tenant() => new(Ecole);

    [Fact]
    public async Task Enseignant_Ne_Voit_Que_Les_Dossiers_De_Ses_Classes_Assignees()
    {
        await using var ctx = Ctx();
        var user = CurrentUser.Enseignant(CompteProf);
        var handler = new GetExamDossiersQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, user));

        var result = await handler.Handle(new GetExamDossiersQuery(), CancellationToken.None);

        result.Items.Should().ContainSingle(d => d.Id == _dossierAssigne);
        result.Items.Should().NotContain(d => d.Id == _dossierNonAssigne);
    }

    [Fact]
    public async Task Directeur_Voit_Tous_Les_Dossiers_Sans_Restriction()
    {
        await using var ctx = Ctx();
        var user = CurrentUser.Directeur();
        var handler = new GetExamDossiersQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, user));

        var result = await handler.Handle(new GetExamDossiersQuery(), CancellationToken.None);

        result.Items.Should().Contain(d => d.Id == _dossierAssigne);
        result.Items.Should().Contain(d => d.Id == _dossierNonAssigne);
    }

    [Fact]
    public async Task Enseignant_Demandant_Un_ClassroomId_Hors_Portee_Est_Refuse()
    {
        await using var ctx = Ctx();
        var user = CurrentUser.Enseignant(CompteProf);
        var handler = new GetExamDossiersQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, user));

        var act = async () => await handler.Handle(
            new GetExamDossiersQuery { ClassroomId = ClasseNonAssignee }, CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task Enseignant_Consulte_Le_Dossier_De_Sa_Classe()
    {
        await using var ctx = Ctx();
        var user = CurrentUser.Enseignant(CompteProf);
        var handler = new GetExamDossierDetailQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, user));

        var result = await handler.Handle(new GetExamDossierDetailQuery(_dossierAssigne), CancellationToken.None);

        result.Id.Should().Be(_dossierAssigne);
    }

    [Fact]
    public async Task Enseignant_Ne_Consulte_Pas_Un_Dossier_Hors_De_Ses_Classes()
    {
        await using var ctx = Ctx();
        var user = CurrentUser.Enseignant(CompteProf);
        var handler = new GetExamDossierDetailQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, user));

        var act = async () => await handler.Handle(new GetExamDossierDetailQuery(_dossierNonAssigne), CancellationToken.None);

        await act.Should().ThrowAsync<UnauthorizedAccessException>(
            "une URL tapée directement doit renvoyer un refus, pas une absence de lien");
    }

    [Fact]
    public async Task Compte_Enseignant_Sans_Fiche_Est_Refuse_Explicitement()
    {
        await using var ctx = Ctx();
        // Rôle Enseignant, mais aucune fiche Teacher ne référence ce compte.
        var orphelin = CurrentUser.Enseignant(Guid.Parse("dddddddd-0000-0000-0000-0000000000d9"));
        var handler = new GetExamDossiersQueryHandler(ctx, new ExamDossierScopeAuthorizer(ctx, orphelin));

        var act = async () => await handler.Handle(new GetExamDossiersQuery(), CancellationToken.None);

        (await act.Should().ThrowAsync<UnauthorizedAccessException>())
            .Which.Message.Should().Contain("fiche enseignant");
    }
}
