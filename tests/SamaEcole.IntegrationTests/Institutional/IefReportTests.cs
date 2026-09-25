using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Institutional;
using SamaEcole.Application.Institutional.Commands;
using SamaEcole.Application.Institutional.Queries;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Institutional;

file sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
{
    public Guid? CurrentSchoolId => schoolId;
}

/// <summary>
/// Évolution N°7 — cartographie IEF de bout en bout, PostgreSQL réel sous le rôle applicatif : rapport de rentrée
/// (classes × âges × sexe, statuts, hors norme, redoublement, corps professoral), normes d'âge de l'école qui
/// remplacent le modèle, contrôle d'âge à l'inscription, et isolation de grade_age_norms (RLS).
/// </summary>
[Trait("Category", "MultiTenant")]
public class IefReportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("a1111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("a2222222-2222-2222-2222-222222222222");
    private static readonly Guid Annee = Guid.Parse("a1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid Sixieme = Guid.Parse("a1111111-0000-0000-0000-0000000000c6");
    private static readonly Guid Troisieme = Guid.Parse("a1111111-0000-0000-0000-0000000000c3");
    private static readonly Guid Maths = Guid.Parse("a1111111-0000-0000-0000-0000000000e1");
    private static readonly Guid Prof = Guid.Parse("a1111111-0000-0000-0000-0000000000f1");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();
        owner.Schools.AddRange(
            new School { Id = Ecole, Name = "CEM de test", InspectionEducationFormation = "IEF Dakar Plateau" },
            new School { Id = AutreEcole, Name = "Autre CEM" });
        owner.SchoolYears.Add(new SchoolYear
        {
            Id = Annee, SchoolId = Ecole, Label = "2026-2027",
            StartDate = new DateOnly(2026, 10, 1), EndDate = new DateOnly(2027, 7, 15), IsActive = true
        });
        owner.Classrooms.AddRange(
            new Classroom { Id = Sixieme, SchoolId = Ecole, Name = "6e A", Level = "Collège", Cycle = CycleType.College, Capacity = 50 },
            new Classroom { Id = Troisieme, SchoolId = Ecole, Name = "3e A", Level = "Collège", Cycle = CycleType.College, Capacity = 50 });

        // Âges au 31/12/2026 — 6e (norme 11–14) : 12 ans F nouvelle, 12 ans G redoublant, 16 ans F transférée (retard).
        // 3e (norme 14–17) : 15 ans G nouveau.
        Enroll(owner, Sixieme, "Awa", "F", new DateOnly(2014, 3, 1), repeating: false, transferred: false);
        Enroll(owner, Sixieme, "Moussa", "M", new DateOnly(2014, 6, 1), repeating: true, transferred: false);
        Enroll(owner, Sixieme, "Fatou", "F", new DateOnly(2010, 1, 15), repeating: false, transferred: true);
        Enroll(owner, Troisieme, "Ibou", "M", new DateOnly(2011, 5, 1), repeating: false, transferred: false);

        owner.Subjects.Add(new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 });
        owner.Teachers.Add(new Teacher
        {
            Id = Prof, SchoolId = Ecole, Matricule = "ENS-1", FullName = "Moussa Diop", Email = "diop@test.sn",
            BirthDate = new DateOnly(1985, 1, 1), Gender = "M", ProfessionalQualification = ProfessionalQualification.CAEM
        });
        owner.ScheduleSlots.AddRange(
            new ScheduleSlot { SchoolId = Ecole, TeacherId = Prof, ClassroomId = Sixieme, SubjectId = Maths, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(10, 0) },
            new ScheduleSlot { SchoolId = Ecole, TeacherId = Prof, ClassroomId = Troisieme, SubjectId = Maths, DayOfWeek = DayOfWeek.Tuesday, StartTime = new TimeOnly(10, 0), EndTime = new TimeOnly(11, 30) });

        // L'autre école élargit la 6e jusqu'à 20 ans : ce réglage ne doit JAMAIS s'appliquer ici.
        owner.GradeAgeNorms.Add(new GradeAgeNorm { SchoolId = AutreEcole, GradeLevel = "Sixième", MinAge = 5, MaxAge = 20 });
        await owner.SaveChangesAsync();
    }

    private static void Enroll(
        SamaEcole.Persistence.ApplicationDbContext owner, Guid classroomId, string name, string gender, DateOnly birthDate,
        bool repeating, bool transferred)
    {
        var student = new Student
        {
            SchoolId = Ecole, Matricule = $"M-{name}", FullName = name, BirthDate = birthDate, BirthPlace = "Dakar",
            Gender = gender, ClassroomId = classroomId
        };
        owner.Students.Add(student);
        owner.Enrollments.Add(new Enrollment
        {
            SchoolId = Ecole, StudentId = student.Id, SchoolYearId = Annee, ClassroomId = classroomId,
            Type = EnrollmentType.NewEnrollment, IsRepeating = repeating, IsTransferredIn = transferred,
            Status = EnrollmentStatus.Confirmed, ReceiptNumber = $"REC-{name}", TotalDue = 0
        });
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<IefReportDto> ReportAsync()
    {
        await using var db = _db.NewAppContext(Ecole);
        return await new GetIefReportQueryHandler(db, new StubTenantProvider(Ecole), TimeProvider.System)
            .Handle(new GetIefReportQuery(Annee), default);
    }

    [Fact]
    public async Task The_Report_Counts_Classes_By_Age_Sex_Status_And_Norm()
    {
        var report = await ReportAsync();

        report.AgeReferenceDate.Should().Be(new DateOnly(2026, 12, 31));
        report.AgeColumns.Select(c => c.Label).Should().Equal("12 ans", "13 ans", "14 ans", "15 ans", "16 ans");

        var sixieme = report.Classes.Single(c => c.ClassroomName == "6e A");
        sixieme.Should().BeEquivalentTo(new
        {
            GradeLevel = "Sixième", Girls = 2, Boys = 1, Total = 3, New = 1, Repeaters = 1, Transferred = 1,
            Early = 0, Late = 1, NormLabel = "11–14 ans"
        });
        sixieme.Cells[0].Should().Be(new IefAgeCell(Girls: 1, Boys: 1), "Awa et Moussa ont 12 ans");
        sixieme.Cells[4].Should().Be(new IefAgeCell(Girls: 1, Boys: 0), "Fatou a 16 ans");

        report.Classes.Select(c => c.ClassroomName).Should().Equal("6e A", "3e A");
        report.RepetitionByLevel.Single(r => r.Level == "Sixième").RepetitionRate.Should().Be(33.3m);
        report.TotalOutOfNorm.Should().Be(1);
    }

    [Fact]
    public async Task Teachers_Are_Counted_By_Discipline_Diploma_And_Weekly_Hours()
    {
        var report = await ReportAsync();

        report.TeachersByDiscipline.Should().ContainSingle()
            .Which.Should().Be(new IefDisciplineRow("Mathématiques", 1, 1, 0, 3.5m));
        report.Teachers.Single().Should().BeEquivalentTo(new { FullName = "Moussa Diop", WeeklyHours = 3.5m, Disciplines = new[] { "Mathématiques" } });
        report.TeachersByDiploma.Single().Professional.Should().Be(ProfessionalQualification.CAEM);
    }

    [Fact]
    public async Task The_School_Own_Norm_Replaces_The_Template_And_Is_Checked_At_Enrollment()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpsertAgeNormCommandHandler(db, new StubTenantProvider(Ecole))
                .Handle(new UpsertAgeNormCommand("Sixième", 11, 16), default);
        }

        (await ReportAsync()).Classes.Single(c => c.ClassroomName == "6e A").Late.Should().Be(0, "16 ans entre dans 11–16");

        await using var read = _db.NewAppContext(Ecole);
        var check = await new CheckEnrollmentAgeQueryHandler(read, TimeProvider.System)
            .Handle(new CheckEnrollmentAgeQuery(Sixieme, new DateOnly(2008, 1, 1)), default);

        check.Should().BeEquivalentTo(new { GradeLevel = "Sixième", Age = 18, MinAge = 11, MaxAge = 16, Status = AgeNormStatus.Late });
        check.Message.Should().Contain("en retard");

        var norms = await new GetAgeNormsQueryHandler(read).Handle(new GetAgeNormsQuery(), default);
        norms.Single(n => n.GradeLevel == "Sixième").Should().BeEquivalentTo(new { TemplateMaxAge = 14, MaxAge = 16, IsCustom = true });
    }

    [Fact]
    public async Task Resetting_A_Norm_Restores_The_Template()
    {
        await using (var db = _db.NewAppContext(Ecole))
        {
            await new UpsertAgeNormCommandHandler(db, new StubTenantProvider(Ecole)).Handle(new UpsertAgeNormCommand("Sixième", 11, 16), default);
        }

        await using (var db = _db.NewAppContext(Ecole))
        {
            await new ResetAgeNormCommandHandler(db, new TestCurrentUser(Guid.NewGuid())).Handle(new ResetAgeNormCommand("Sixième"), default);
        }

        (await ReportAsync()).Classes.Single(c => c.ClassroomName == "6e A").Late.Should().Be(1);
    }

    [Fact]
    public async Task RawSqlQuery_Should_Never_Return_The_Age_Norms_Of_Another_School()
    {
        await using var connection = await _db.OpenRawAppConnectionAsync(Ecole);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM grade_age_norms;";
        ((long)(await command.ExecuteScalarAsync())!).Should().Be(0, "la RLS masque le réglage de l'autre école");

        command.CommandText = $"""INSERT INTO grade_age_norms ("Id","SchoolId","GradeLevel","MinAge","MaxAge","CreatedAt","IsDeleted") VALUES ('{Guid.NewGuid()}','{AutreEcole}','CI',5,8,NOW(),FALSE);""";
        var act = () => command.ExecuteNonQueryAsync();
        await act.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == PostgresErrorCodes.InsufficientPrivilege);
    }
}
