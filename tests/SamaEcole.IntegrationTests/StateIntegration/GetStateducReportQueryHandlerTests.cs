using FluentAssertions;
using SamaEcole.Application.StateIntegration.Queries.GetStateducReport;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.StateIntegration;

/// <summary>
/// Agrégats du rapport annuel STATEDUC (<see cref="GetStateducReportQueryHandler"/>, Volume 1 §23.3,
/// ticket JGK-M04) contre une vraie base — <see cref="Common.RlsCoverageTests"/> couvre déjà
/// l'isolation multi-tenant de ce module ; ces tests-ci vérifient les TROIS PRINCIPES documentés sur le
/// Handler : périmètre = inscriptions non annulées (pas les élèves en base), âge à la date
/// d'observation, aucune case devinée. Comble le manque consigné dans ACTIVE_CONTEXT.md
/// (« Ce qui reste : agrégats STATEDUC »).
/// </summary>
public class GetStateducReportQueryHandlerTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleC = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid AnneeC = Guid.Parse("aaaa3333-0000-0000-0000-000000000001");
    private static readonly Guid ClasseCm2 = Guid.Parse("cccc3333-0000-0000-0000-000000000001");
    private static readonly Guid ClasseSixieme = Guid.Parse("cccc3333-0000-0000-0000-000000000002");

    private static readonly DateOnly Observation = new(2027, 1, 15);

    private sealed class FixedTenantProvider(Guid schoolId) : SamaEcole.Application.Common.Interfaces.ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.Add(new School { Id = EcoleC, Name = "École C" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeC, SchoolId = EcoleC, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 31), IsActive = true
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseCm2, SchoolId = EcoleC, Name = "CM2 A", Level = "CM2", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseSixieme, SchoolId = EcoleC, Name = "6e A", Level = "6e", Cycle = CycleType.College, Capacity = 40 });

        // ---- Élèves --------------------------------------------------------------------------
        var awa = Guid.NewGuid();     // CM2, fille, IEN renseigné, non redoublante — 10 ans à l'observation.
        var moussa = Guid.NewGuid();  // CM2, garçon, SANS IEN, redoublant.
        var fatou = Guid.NewGuid();   // 6e, fille, IEN renseigné.
        var omar = Guid.NewGuid();    // 6e, garçon — inscription ANNULÉE : ne doit compter nulle part.
        var khadija = Guid.NewGuid(); // CM2, fille — date de naissance implausible (> 30 ans) : âge non déterminé.

        owner.Students.AddRange(
            new Student { Id = awa, SchoolId = EcoleC, Matricule = "STD-001", FullName = "Awa Fall", BirthDate = new DateOnly(2016, 6, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseCm2, IenNumber = "P01234726000175" },
            new Student { Id = moussa, SchoolId = EcoleC, Matricule = "STD-002", FullName = "Moussa Sarr", BirthDate = new DateOnly(2015, 9, 1), BirthPlace = "Dakar", Gender = "M", ClassroomId = ClasseCm2 },
            new Student { Id = fatou, SchoolId = EcoleC, Matricule = "STD-003", FullName = "Fatou Diop", BirthDate = new DateOnly(2013, 3, 1), BirthPlace = "Thiès", Gender = "F", ClassroomId = ClasseSixieme, IenNumber = "P01234726000267" },
            new Student { Id = omar, SchoolId = EcoleC, Matricule = "STD-004", FullName = "Omar Ba", BirthDate = new DateOnly(2013, 4, 1), BirthPlace = "Thiès", Gender = "M", ClassroomId = ClasseSixieme },
            new Student { Id = khadija, SchoolId = EcoleC, Matricule = "STD-005", FullName = "Khadija Sy", BirthDate = new DateOnly(1990, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = ClasseCm2 });

        // ---- Inscriptions (le périmètre réel du rapport, pas la table Students) --------------
        owner.Enrollments.AddRange(
            NewEnrollment(awa, ClasseCm2, isRepeating: false, EnrollmentStatus.Confirmed),
            NewEnrollment(moussa, ClasseCm2, isRepeating: true, EnrollmentStatus.Confirmed),
            NewEnrollment(fatou, ClasseSixieme, isRepeating: false, EnrollmentStatus.Confirmed),
            NewEnrollment(omar, ClasseSixieme, isRepeating: false, EnrollmentStatus.Cancelled),
            NewEnrollment(khadija, ClasseCm2, isRepeating: false, EnrollmentStatus.Confirmed));

        // ---- Enseignants ----------------------------------------------------------------------
        owner.Teachers.AddRange(
            new Teacher
            {
                SchoolId = EcoleC, Matricule = "ENS-001", FullName = "Ndèye Diallo", Email = "ndeye@example.test",
                BirthDate = new DateOnly(1985, 1, 1), Gender = "F", Status = EntityStatus.Active,
                AcademicQualification = AcademicQualification.Licence,
                ProfessionalQualification = ProfessionalQualification.CAEM,
                CivilServiceStatus = TeacherCivilServiceStatus.Fonctionnaire
            },
            new Teacher
            {
                SchoolId = EcoleC, Matricule = "ENS-002", FullName = "Ibrahima Sow", Email = "ibrahima@example.test",
                BirthDate = new DateOnly(1980, 1, 1), Gender = "M", Status = EntityStatus.Active,
                AcademicQualification = AcademicQualification.NonRenseigne,
                ProfessionalQualification = ProfessionalQualification.NonRenseigne,
                CivilServiceStatus = TeacherCivilServiceStatus.Vacataire
            },
            new Teacher
            {
                // Genre non saisi : troisième colonne, jamais imputée à Hommes ni à Femmes.
                SchoolId = EcoleC, Matricule = "ENS-003", FullName = "A. Ka", Email = "aka@example.test",
                BirthDate = new DateOnly(1990, 1, 1), Gender = null, Status = EntityStatus.Active,
                AcademicQualification = AcademicQualification.Master,
                ProfessionalQualification = ProfessionalQualification.CAES,
                CivilServiceStatus = TeacherCivilServiceStatus.Contractuel
            },
            new Teacher
            {
                // NON ACTIF (suspendu) : ne doit compter dans aucun des deux tableaux de personnel —
                // le formulaire décrit le personnel EN POSTE.
                SchoolId = EcoleC, Matricule = "ENS-004", FullName = "Ancien Prof", Email = "ancien@example.test",
                BirthDate = new DateOnly(1975, 1, 1), Gender = "M", Status = EntityStatus.Suspended,
                AcademicQualification = AcademicQualification.BAC,
                ProfessionalQualification = ProfessionalQualification.CAP,
                CivilServiceStatus = TeacherCivilServiceStatus.Fonctionnaire
            });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Enrollment NewEnrollment(Guid studentId, Guid classroomId, bool isRepeating, EnrollmentStatus status) => new()
    {
        SchoolId = EcoleC,
        StudentId = studentId,
        SchoolYearId = AnneeC,
        ClassroomId = classroomId,
        Type = EnrollmentType.NewEnrollment,
        IsRepeating = isRepeating,
        Status = status,
        ReceiptNumber = $"RCP-{studentId:N}"[..12],
        EnrolledAt = new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero)
    };

    private async Task<SamaEcole.Application.StateIntegration.StateducReportDto> RunAsync()
    {
        await using var ctx = _db.NewAppContext(EcoleC);
        var handler = new GetStateducReportQueryHandler(ctx, new FixedTenantProvider(EcoleC), TimeProvider.System);
        return await handler.Handle(new GetStateducReportQuery(AnneeC, Observation), CancellationToken.None);
    }

    [Fact]
    public async Task Cancelled_Enrollment_Is_Excluded_From_Every_Table()
    {
        var report = await RunAsync();

        // 4 élèves comptés (Awa, Moussa, Fatou, Khadija) — Omar (annulé) n'apparaît NULLE PART, ni
        // dans l'effectif, ni dans la pyramide des âges.
        report.TotalStudents.Should().Be(4);
    }

    [Fact]
    public async Task Enrollments_Are_Grouped_By_Level_With_Repeaters_And_Missing_Ien()
    {
        var report = await RunAsync();

        var cm2 = report.EnrollmentsByLevel.Should().ContainSingle(r => r.Level == "CM2").Subject;
        cm2.Boys.Should().Be(1);   // Moussa
        cm2.Girls.Should().Be(2);  // Awa, Khadija
        cm2.Repeaters.Should().Be(1); // Moussa
        cm2.WithoutIen.Should().Be(2); // Moussa, Khadija
        cm2.ClassroomCount.Should().Be(1);

        var sixieme = report.EnrollmentsByLevel.Should().ContainSingle(r => r.Level == "6e").Subject;
        sixieme.Girls.Should().Be(1);  // Fatou
        sixieme.Boys.Should().Be(0);   // Omar exclu (annulé)
        sixieme.WithoutIen.Should().Be(0);
    }

    [Fact]
    public async Task Age_Is_Computed_At_The_Observation_Date_Not_At_Generation_Time()
    {
        var report = await RunAsync();

        // Awa : née le 2016-06-01, observée au 2027-01-15 -> 10 ans révolus.
        report.AgePyramid.Should().ContainSingle(a => a.Age == 10)
            .Subject.Girls.Should().Be(1);
    }

    [Fact]
    public async Task Implausible_Birth_Date_Falls_Into_The_Undetermined_Age_Bracket()
    {
        var report = await RunAsync();

        // Khadija : née en 1990, plus de 30 ans avant l'observation -> tranche "non déterminé", jamais
        // fondue dans une tranche d'âge réelle qui fausserait la pyramide.
        var undetermined = report.AgePyramid.Should().ContainSingle(a => a.Age == null).Subject;
        undetermined.Girls.Should().Be(1);
        undetermined.Label.Should().Be("Âge non déterminé");
    }

    [Fact]
    public async Task Inactive_Teacher_Is_Excluded_From_Both_Personnel_Tables()
    {
        var report = await RunAsync();

        // 3 enseignants actifs comptés ; le 4e (suspendu) n'apparaît dans aucun des deux tableaux.
        report.TotalTeachers.Should().Be(3);
        report.TeacherStatuses.Sum(s => s.Count).Should().Be(3);
    }

    [Fact]
    public async Task Teacher_Without_Gender_Is_Counted_Separately_Never_Imputed()
    {
        var report = await RunAsync();

        report.TeachersWithoutGender.Should().Be(1, "A. Ka n'a pas de genre saisi");

        var qualified = report.TeacherQualifications.Should()
            .ContainSingle(r => r.ProfessionalQualification == ProfessionalQualification.CAES).Subject;
        qualified.Men.Should().Be(0);
        qualified.Women.Should().Be(0);
        qualified.GenderNotReported.Should().Be(1);
    }

    [Fact]
    public async Task Unreported_Qualification_Is_Counted_Separately_From_Qualified_Teachers()
    {
        var report = await RunAsync();

        // Ibrahima Sow (NonRenseigne/NonRenseigne) ne doit ni gonfler QualifiedTeachers ni disparaître.
        report.UnreportedQualificationTeachers.Should().Be(1);
        report.QualifiedTeachers.Should().Be(2, "Ndèye Diallo (CAEM) et A. Ka (CAES) sont qualifiés");
    }
}
