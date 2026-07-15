using FluentAssertions;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace SamaEcole.IntegrationTests.Enrollments;

/// <summary>
/// Ticket JGK-E01 — l'inscription calcule-t-elle RÉELLEMENT le bon montant, génère-t-elle le
/// matricule sans trou, et l'isolation tient-elle ? On exerce le vrai Handler contre un PostgreSQL
/// réel sous le rôle applicatif (RLS active), pas seulement le DbContext.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid EcoleA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("aaaaaaaa-0000-0000-0000-00000000000a");
    private static readonly Guid AnneeA = Guid.Parse("bbbbbbbb-0000-0000-0000-00000000000b");
    private static readonly Guid CatInscription = Guid.Parse("cccccccc-0000-0000-0000-00000000000c");
    private static readonly Guid CatMensualite = Guid.Parse("dddddddd-0000-0000-0000-00000000000d");

    private const int TuitionMonths = 10; // volontairement ≠ défaut (9) : prouve que le RÉGLAGE est lu.
    private const decimal Inscription = 10_000m;
    private const decimal Mensualite = 15_000m;

    private readonly int _year = AcademicYear.ForDate(DateTimeOffset.UtcNow);

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();

        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "École A", Phone = "77 123 45 67" },
            new School { Id = EcoleB, Name = "École B" });

        owner.Classrooms.Add(new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "CM2", Level = "Primaire", Capacity = 40 });

        owner.SchoolYears.Add(new SchoolYear
        {
            Id = AnneeA,
            SchoolId = EcoleA,
            Label = $"{_year}-{_year + 1}",
            StartDate = new DateOnly(_year, 10, 1),
            EndDate = new DateOnly(_year + 1, 6, 30),
            IsActive = true
        });

        owner.FeeCategories.AddRange(
            new FeeCategory { Id = CatInscription, SchoolId = EcoleA, Name = "Inscription", IsRecurring = false },
            new FeeCategory { Id = CatMensualite, SchoolId = EcoleA, Name = "Mensualité", IsRecurring = true });

        owner.ClassFees.AddRange(
            new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatInscription, ClassroomId = ClasseA, Amount = Inscription },
            new ClassFee { SchoolId = EcoleA, FeeCategoryId = CatMensualite, ClassroomId = ClasseA, Amount = Mensualite });

        owner.SchoolSettings.Add(new SchoolSettings { SchoolId = EcoleA, TuitionMonthsPerYear = TuitionMonths });

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private CreateEnrollmentCommandHandler NewHandler(ApplicationDbContext db, Guid schoolId) =>
        new(db, new StubTenantProvider(schoolId), _db.NewGenerator(db), TimeProvider.System);

    private static CreateEnrollmentCommand NewStudentCommand(string fullName) => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = ClasseA,
        FullName = fullName,
        BirthDate = new DateOnly(2015, 5, 20),
        Gender = "F"
    };

    [Fact]
    public async Task A_New_Enrollment_Computes_TotalDue_From_The_Class_Fee_Schedule()
    {
        await using var db = _db.NewAppContext(EcoleA);

        var receipt = await NewHandler(db, EcoleA).Handle(NewStudentCommand("Awa Ndiaye"), CancellationToken.None);

        // Un frais ponctuel compté une fois + une mensualité × le nombre de mensualités de l'année.
        receipt.TotalDue.Should().Be(Inscription + Mensualite * TuitionMonths);
        receipt.Matricule.Should().Be($"ELEV-{_year}-0001", "le matricule est généré à l'enregistrement (JGK-D01/B02)");
        receipt.Lines.Should().HaveCount(2);

        var mensualite = receipt.Lines.Single(l => l.IsRecurring);
        mensualite.Months.Should().Be(TuitionMonths);
        mensualite.LineTotal.Should().Be(Mensualite * TuitionMonths);

        var inscription = receipt.Lines.Single(l => !l.IsRecurring);
        inscription.Months.Should().Be(1, "un frais ponctuel n'est jamais multiplié");
    }

    [Fact]
    public async Task The_Fee_Lines_Are_Persisted_As_A_Frozen_Snapshot()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db, EcoleA).Handle(NewStudentCommand("Modou Fall"), CancellationToken.None);

        await using var check = _db.NewAppContext(EcoleA);
        var lines = await check.EnrollmentFeeLines
            .Where(l => l.EnrollmentId == receipt.EnrollmentId)
            .ToListAsync();

        lines.Should().HaveCount(2);
        lines.Sum(l => l.LineTotal).Should().Be(receipt.TotalDue);
        lines.Should().Contain(l => l.Designation == "Mensualité" && l.Months == TuitionMonths);
    }

    [Fact]
    public async Task Successive_New_Enrollments_Get_Strictly_Sequential_Matricules()
    {
        await using var db = _db.NewAppContext(EcoleA);
        var handler = NewHandler(db, EcoleA);

        var first = await handler.Handle(NewStudentCommand("Élève Un"), CancellationToken.None);
        var second = await handler.Handle(NewStudentCommand("Élève Deux"), CancellationToken.None);

        first.Matricule.Should().Be($"ELEV-{_year}-0001");
        second.Matricule.Should().Be($"ELEV-{_year}-0002");
    }

    [Fact]
    public async Task Enrolling_The_Same_Student_Twice_In_The_Active_Year_Is_Refused()
    {
        // Première inscription : crée l'élève.
        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db, EcoleA).Handle(NewStudentCommand("Fatou Sarr"), CancellationToken.None);

        var studentId = await db.Students.Where(s => s.Matricule == receipt.Matricule).Select(s => s.Id).FirstAsync();

        // Réinscription du MÊME élève sur la MÊME année active : « une inscription active par année ».
        await using var db2 = _db.NewAppContext(EcoleA);
        var act = async () => await NewHandler(db2, EcoleA).Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = ClasseA,
            StudentId = studentId
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task A_Re_Enrollment_Reuses_The_Existing_Student_Without_A_New_Matricule()
    {
        // Un élève déjà connu, inscrit une année passée (donc aucune inscription sur l'année active).
        var studentId = Guid.CreateVersion7();
        const string existingMatricule = "ELEV-2020-0007";
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Students.Add(new Student
            {
                Id = studentId,
                SchoolId = EcoleA,
                Matricule = existingMatricule,
                FullName = "Ancien Élève",
                BirthDate = new DateOnly(2012, 3, 3),
                Gender = "M",
                ClassroomId = ClasseA
            });
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        await using var db = _db.NewAppContext(EcoleA);
        var receipt = await NewHandler(db, EcoleA).Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.ReEnrollment,
            ClassroomId = ClasseA,
            StudentId = studentId
        }, CancellationToken.None);

        receipt.Matricule.Should().Be(existingMatricule, "une réinscription ne régénère jamais le matricule");
        receipt.Type.Should().Be(nameof(EnrollmentType.ReEnrollment));

        await using var check = _db.NewAppContext(EcoleA);
        (await check.Students.CountAsync()).Should().Be(1, "aucun nouvel élève n'est créé à la réinscription");
    }

    [Fact]
    public async Task Enrolling_Without_An_Active_Year_Is_Refused()
    {
        // École B n'a aucune année active : rien à rattacher.
        await using var db = _db.NewAppContext(EcoleB);
        var act = async () => await NewHandler(db, EcoleB).Handle(new CreateEnrollmentCommand
        {
            Type = EnrollmentType.NewEnrollment,
            ClassroomId = ClasseA,
            FullName = "Sans Année",
            BirthDate = new DateOnly(2015, 1, 1),
            Gender = "M"
        }, CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task An_Enrollment_Of_One_School_Is_Invisible_To_Another_At_The_Database_Level()
    {
        await using (var db = _db.NewAppContext(EcoleA))
        {
            await NewHandler(db, EcoleA).Handle(NewStudentCommand("Isolée Diop"), CancellationToken.None);
        }

        // SQL brut sous le rôle applicatif, session tenant = École B : seule la policy RLS fait foi.
        await using var connection = await _db.OpenRawAppConnectionAsync(EcoleB);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM enrollments;";

        var visibleToB = (long)(await command.ExecuteScalarAsync())!;
        visibleToB.Should().Be(0, "une inscription d'une autre école ne doit jamais être visible (règle #2)");
    }

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }
}
