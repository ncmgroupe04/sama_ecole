using FluentAssertions;
using SamaEcole.Application.Exams.Queries.GetCfeeCandidates;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Exams;

/// <summary>
/// Ticket Onboarding #6 (profil Élémentaire) — cohorte CM2 de l'année active, fusionnée avec les
/// dossiers CFEE déjà ouverts. Le point le plus fragile de cette query est le filtre d'appartenance :
/// <see cref="Student.ClassroomId"/> seul ne suffit pas (voir <see cref="Excludes_Student_Not_Reenrolled_This_Year"/>).
/// </summary>
public class GetCfeeCandidatesTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid AnneeActive = Guid.Parse("aaaa1111-0000-0000-0000-000000000001");
    private static readonly Guid AnneePrecedente = Guid.Parse("aaaa1111-0000-0000-0000-000000000002");
    private static readonly Guid ClasseCm2 = Guid.Parse("cccccccc-0000-0000-0000-000000000001");
    private static readonly Guid ClasseCm1 = Guid.Parse("cccccccc-0000-0000-0000-000000000002");
    private static readonly Guid ClasseTerminale = Guid.Parse("cccccccc-0000-0000-0000-000000000003");

    private static readonly Guid EleveSansDossier = Guid.Parse("eeeeeeee-0000-0000-0000-000000000001");
    private static readonly Guid EleveAvecDossier = Guid.Parse("eeeeeeee-0000-0000-0000-000000000002");
    private static readonly Guid EleveNonReinscrit = Guid.Parse("eeeeeeee-0000-0000-0000-000000000003");
    private static readonly Guid EleveCm1 = Guid.Parse("eeeeeeee-0000-0000-0000-000000000004");
    private static readonly Guid EleveTerminale = Guid.Parse("eeeeeeee-0000-0000-0000-000000000005");

    private static readonly Guid SessionCfee = Guid.Parse("55550001-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleA, Name = "École A" });

        owner.SchoolYears.AddRange(
            new SchoolYear { Id = AnneeActive, SchoolId = EcoleA, Label = "2026-2027", StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true },
            new SchoolYear { Id = AnneePrecedente, SchoolId = EcoleA, Label = "2025-2026", StartDate = new DateOnly(2025, 10, 1), EndDate = new DateOnly(2026, 7, 31), IsActive = false });

        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseCm2, SchoolId = EcoleA, Name = "CM2 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseCm1, SchoolId = EcoleA, Name = "CM1 A", Level = "Primaire", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseTerminale, SchoolId = EcoleA, Name = "Terminale S2", Level = "Lycée", Cycle = CycleType.Lycee, Capacity = 40 });

        owner.Students.AddRange(
            new Student { Id = EleveSansDossier, SchoolId = EcoleA, Matricule = "ELEV-0001", FullName = "Awa Fall", BirthDate = new DateOnly(2015, 3, 12), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseCm2 },
            new Student { Id = EleveAvecDossier, SchoolId = EcoleA, Matricule = "ELEV-0002", FullName = "Moussa Ba", BirthDate = new DateOnly(2015, 5, 20), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseCm2 },
            // ClassroomId pointe encore vers CM2 (dernière classe connue l'an dernier), mais AUCUNE
            // inscription vivante pour l'année ACTIVE — c'est le cas que Student.ClassroomId seul ne
            // sait pas trancher (voir la doc du Handler).
            new Student { Id = EleveNonReinscrit, SchoolId = EcoleA, Matricule = "ELEV-0003", FullName = "Fatou Sy", BirthDate = new DateOnly(2014, 8, 1), BirthPlace = "Kaolack", Gender = "F", ClassroomId = ClasseCm2 },
            new Student { Id = EleveCm1, SchoolId = EcoleA, Matricule = "ELEV-0004", FullName = "Ibrahima Ndoye", BirthDate = new DateOnly(2016, 2, 2), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseCm1 },
            new Student { Id = EleveTerminale, SchoolId = EcoleA, Matricule = "ELEV-0005", FullName = "Aissatou Diop", BirthDate = new DateOnly(2009, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseTerminale });

        owner.Enrollments.AddRange(
            new Enrollment { SchoolId = EcoleA, StudentId = EleveSansDossier, SchoolYearId = AnneeActive, ClassroomId = ClasseCm2, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0001", EnrolledAt = DateTimeOffset.UtcNow },
            new Enrollment { SchoolId = EcoleA, StudentId = EleveAvecDossier, SchoolYearId = AnneeActive, ClassroomId = ClasseCm2, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0002", EnrolledAt = DateTimeOffset.UtcNow },
            // Seule inscription de cet élève : l'an dernier, jamais réinscrit cette année.
            new Enrollment { SchoolId = EcoleA, StudentId = EleveNonReinscrit, SchoolYearId = AnneePrecedente, ClassroomId = ClasseCm2, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0003", EnrolledAt = DateTimeOffset.UtcNow },
            new Enrollment { SchoolId = EcoleA, StudentId = EleveCm1, SchoolYearId = AnneeActive, ClassroomId = ClasseCm1, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0004", EnrolledAt = DateTimeOffset.UtcNow },
            new Enrollment { SchoolId = EcoleA, StudentId = EleveTerminale, SchoolYearId = AnneeActive, ClassroomId = ClasseTerminale, Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, TotalDue = 0, ReceiptNumber = "REC-0005", EnrolledAt = DateTimeOffset.UtcNow });

        owner.ExamSessions.Add(
            new ExamSession { Id = SessionCfee, SchoolId = EcoleA, SchoolYearId = AnneeActive, ExamType = ExamType.CFEE });

        owner.ExamDossiers.Add(new ExamDossier
        {
            SchoolId = EcoleA,
            ExamSessionId = SessionCfee,
            StudentId = EleveAvecDossier,
            ClassroomId = ClasseCm2,
            BirthCertificatePresent = true,
            CivilStatusConforming = true,
            PhotoPresent = true,
            FeeReceiptPresent = false,
            Status = ExamDossierStatus.Incomplet
        });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Includes_Cm2_Students_With_And_Without_A_Dossier()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        result.SchoolYearLabel.Should().Be("2026-2027");
        result.CfeeExamSessionId.Should().Be(SessionCfee);
        result.Candidates.Should().HaveCount(2)
            .And.Contain(c => c.StudentId == EleveSansDossier && !c.HasDossier)
            .And.Contain(c => c.StudentId == EleveAvecDossier && c.HasDossier);
    }

    [Fact]
    public async Task Merges_The_Existing_Cfee_Dossier_Checklist_And_Status()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        var candidate = result.Candidates.Single(c => c.StudentId == EleveAvecDossier);
        candidate.Status.Should().Be(nameof(ExamDossierStatus.Incomplet));
        candidate.BirthCertificatePresent.Should().BeTrue();
        candidate.PhotoPresent.Should().BeTrue();
        candidate.FeeReceiptPresent.Should().BeFalse();
        candidate.DossierId.Should().NotBeNull();
        candidate.RowVersion.Should().NotBeNull();
    }

    [Fact]
    public async Task Excludes_Student_Not_Reenrolled_This_Year()
    {
        // Le cas central de cette query : ClassroomId de cet élève pointe TOUJOURS vers la classe CM2
        // (dernière connue), mais sa seule inscription est celle de l'année PRÉCÉDENTE — il ne doit
        // donc jamais apparaître comme candidat CM2 de l'année active.
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        result.Candidates.Should().NotContain(c => c.StudentId == EleveNonReinscrit);
    }

    [Fact]
    public async Task Excludes_Students_Outside_Cm2()
    {
        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        result.Candidates.Should().NotContain(c => c.StudentId == EleveCm1)
            .And.NotContain(c => c.StudentId == EleveTerminale);
    }

    [Fact]
    public async Task Returns_Candidates_Without_A_Dossier_When_No_Cfee_Session_Exists_Yet()
    {
        // Bascule l'année active sur AnneePrecedente, où AUCUNE session CFEE n'a encore été ouverte.
        // EleveNonReinscrit y a justement sa seule inscription vivante (voir InitializeAsync) : il doit
        // donc apparaître ici comme candidat CM2 SANS dossier — preuve symétrique de
        // Excludes_Student_Not_Reenrolled_This_Year (même élève, exclu de l'année active, inclus de sa
        // propre année). CfeeExamSessionId reste null : rien ne doit planter faute de session ouverte,
        // à charge du front de proposer d'en créer une (goCreateCfeeSession, exams.js).
        //
        // Contexte APP (tenant EcoleA), pas propriétaire : le Global Query Filter EF du propriétaire
        // ne connaît aucun tenant courant (StubTenantProvider(null)) et ne verrait donc aucune ligne
        // à lire — seul son INSERT contourne la RLS, ses lectures doivent passer par l'app (voir
        // ExamWorkflowTests.RowVersionAsync, même convention).
        await using (var setupCtx = _db.NewAppContext(EcoleA))
        {
            var active = await setupCtx.SchoolYears.FindAsync([AnneeActive], CancellationToken.None);
            var precedente = await setupCtx.SchoolYears.FindAsync([AnneePrecedente], CancellationToken.None);
            active!.IsActive = false;
            precedente!.IsActive = true;
            await setupCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        result.SchoolYearLabel.Should().Be("2025-2026");
        result.CfeeExamSessionId.Should().BeNull();
        result.Candidates.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { StudentId = EleveNonReinscrit, HasDossier = false });
    }

    [Fact]
    public async Task Returns_Empty_Result_When_No_Active_School_Year()
    {
        await using (var setupCtx = _db.NewAppContext(EcoleA))
        {
            var active = await setupCtx.SchoolYears.FindAsync([AnneeActive], CancellationToken.None);
            active!.IsActive = false;
            await setupCtx.SaveChangesAsync(CancellationToken.None);
        }

        await using var ctx = _db.NewAppContext(EcoleA);
        var handler = new GetCfeeCandidatesQueryHandler(ctx);

        var result = await handler.Handle(new GetCfeeCandidatesQuery(), CancellationToken.None);

        result.SchoolYearLabel.Should().BeNull();
        result.CfeeExamSessionId.Should().BeNull();
        result.Candidates.Should().BeEmpty();
    }
}
