using System.Text;
using FluentAssertions;
using MediatR;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Application.Internat.Queries.GetProgressDashboard;
using SamaEcole.Application.Internat.Queries.GetStudentHizbProgress;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReport;
using SamaEcole.Application.Internat.Queries.GetStudentHizbReportPdf;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.Daara;

/// <summary>
/// Tableau de bord de la mémorisation (Direction) et bulletin coranique PDF, par les VRAIS Handlers sur un vrai
/// PostgreSQL sous le rôle applicatif : les agrégats, les tranches et la stagnation sont-ils justes, l'isolation entre
/// écoles tient-elle, et un Oustaz ne tire-t-il que le bulletin de SES élèves ?
/// </summary>
[Trait("Category", "MultiTenant")]
public class ProgressDashboardAndReportTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly DateTimeOffset Now = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    private static readonly Guid EcoleA = Guid.Parse("d1111111-1111-1111-1111-111111111111");
    private static readonly Guid EcoleB = Guid.Parse("d2222222-2222-2222-2222-222222222222");
    private static readonly Guid ClasseA = Guid.Parse("d1111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseB = Guid.Parse("d2222222-0000-0000-0000-0000000000c1");

    private static readonly Guid UserOustaz1 = Guid.Parse("d1111111-0000-0000-0000-0000000000a1");
    private static readonly Guid UserDirecteur = Guid.Parse("d1111111-0000-0000-0000-0000000000a5");
    private static readonly Guid Oustaz1 = Guid.Parse("d1111111-0000-0000-0000-0000000000f1");
    private static readonly Guid Oustaz2 = Guid.Parse("d1111111-0000-0000-0000-0000000000f2");
    private static readonly Guid OustazB = Guid.Parse("d2222222-0000-0000-0000-0000000000f1");

    private static readonly Guid Recent = Guid.Parse("d1111111-0000-0000-0000-0000000000e1");     // Oustaz1, évalué il y a 5 jours
    private static readonly Guid Jamais = Guid.Parse("d1111111-0000-0000-0000-0000000000e2");     // Oustaz1, jamais évalué
    private static readonly Guid Ancien = Guid.Parse("d1111111-0000-0000-0000-0000000000e3");     // Oustaz2, évalué il y a 60 jours
    private static readonly Guid Termine = Guid.Parse("d1111111-0000-0000-0000-0000000000e4");    // Oustaz2, a terminé les 60 Hizb
    private static readonly Guid SansHalqa = Guid.Parse("d1111111-0000-0000-0000-0000000000e5");  // aucun Oustaz
    private static readonly Guid EleveB = Guid.Parse("d2222222-0000-0000-0000-0000000000e1");     // autre école

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(
            new School { Id = EcoleA, Name = "Daara Al Azhar", Address = "Touba, Sénégal" },
            new School { Id = EcoleB, Name = "Daara B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = ClasseA, SchoolId = EcoleA, Name = "Halqa A", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 },
            new Classroom { Id = ClasseB, SchoolId = EcoleB, Name = "Halqa B", Level = "Daara", Cycle = CycleType.Primaire, Capacity = 40 });
        owner.Users.AddRange(
            new User { Id = UserOustaz1, SchoolId = EcoleA, Email = "oustaz1@daara.sn", PasswordHash = "x", FullName = "Oustaz Un", Role = Role.Enseignant },
            new User { Id = UserDirecteur, SchoolId = EcoleA, Email = "dir@daara.sn", PasswordHash = "x", FullName = "Directeur", Role = Role.Directeur });
        await owner.SaveChangesAsync();

        owner.Instructors.AddRange(
            new Instructor { Id = Oustaz1, SchoolId = EcoleA, FullName = "Serigne Modou", FullNameAr = "سيرين مودو", UserId = UserOustaz1 },
            new Instructor { Id = Oustaz2, SchoolId = EcoleA, FullName = "Oustaz Fall" },
            new Instructor { Id = OustazB, SchoolId = EcoleB, FullName = "Oustaz B" });
        await owner.SaveChangesAsync();

        owner.Students.AddRange(
            Student(Recent, EcoleA, ClasseA, "Awa Diop", "عائشة جوب", "ELEV-A1", Oustaz1),
            Student(Jamais, EcoleA, ClasseA, "Binta Fall", null, "ELEV-A2", Oustaz1),
            Student(Ancien, EcoleA, ClasseA, "Cheikh Ndiaye", null, "ELEV-A3", Oustaz2),
            Student(Termine, EcoleA, ClasseA, "Demba Sow", null, "ELEV-A4", Oustaz2),
            Student(SansHalqa, EcoleA, ClasseA, "Fatou Sarr", null, "ELEV-A5", null),
            Student(EleveB, EcoleB, ClasseB, "Élève B", null, "ELEV-B1", OustazB));
        await owner.SaveChangesAsync();

        owner.StudentHizbStatuses.AddRange(
            Hizb(Recent, 1, 4, 5, Now.AddDays(-5)),       // complet
            Hizb(Recent, 2, 2, 3, Now.AddDays(-5)),       // en cours  → 6 quarts = 2,5 %
            Hizb(Ancien, 1, 3, 4, Now.AddDays(-60)));     // 3 quarts = 1,3 % (1,25 arrondi loin de zéro)
        for (var n = 1; n <= 60; n++)
        {
            owner.StudentHizbStatuses.Add(Hizb(Termine, n, 4, 5, Now.AddDays(-200)));   // 240 quarts = 100 %, évalué il y a longtemps
        }

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Student Student(Guid id, Guid school, Guid classroom, string name, string? nameAr, string matricule, Guid? instructor) =>
        new()
        {
            Id = id, SchoolId = school, Matricule = matricule, FullName = name, FullNameAr = nameAr,
            BirthDate = new DateOnly(2012, 1, 1), BirthPlace = "Touba", Gender = "M", ClassroomId = classroom, InstructorId = instructor
        };

    private static StudentHizbStatus Hizb(Guid student, int number, int quarters, int rating, DateTimeOffset evaluatedAt) =>
        new()
        {
            SchoolId = EcoleA, StudentId = student, HizbNumber = number, CompletedQuarters = quarters,
            State = HizbRules.StateFor(quarters), Rating = rating, LastEvaluatedAt = evaluatedAt
        };

    // ------------------------------------------------------------------ outillage

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class NoLogoProvider : ISchoolLogoProvider
    {
        public Task<byte[]?> TryFetchAsync(string? logoUrl, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);
    }

    /// <summary>
    /// ISender minimal : route les deux requêtes que le bulletin enchaîne vers leurs VRAIS Handlers (même contexte, même
    /// compte courant), sans monter tout MediatR.
    /// </summary>
    private sealed class ChainSender(ApplicationDbContext db, HalqaScopeAuthorizer scope) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetStudentHizbProgressQuery q => new GetStudentHizbProgressQueryHandler(db, scope).Handle(q, cancellationToken),
                GetStudentHizbReportQuery q => new GetStudentHizbReportQueryHandler(this, db, new FixedTimeProvider(Now)).Handle(q, cancellationToken),
                _ => throw new NotSupportedException(request.GetType().Name)
            };
            return (Task<TResponse>)result;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private async Task<ProgressDashboardDto> DashboardAsync(Guid school, int staleDays = 30)
    {
        await using var db = _db.NewAppContext(school);
        return await new GetProgressDashboardQueryHandler(db, new FixedTimeProvider(Now))
            .Handle(new GetProgressDashboardQuery(staleDays), CancellationToken.None);
    }

    // ------------------------------------------------------------------ tableau de bord

    [Fact]
    public async Task The_Dashboard_Counts_Only_Students_Attached_To_A_Halqa()
    {
        var d = await DashboardAsync(EcoleA);

        d.StudentsInHalqa.Should().Be(4);
        d.UnassignedStudents.Should().Be(1);
        d.CompletedHizbs.Should().Be(61, "1 (Awa) + 60 (Demba)");
        d.CompletedQuarters.Should().Be(6 + 3 + 240);
        d.AverageProgressPercent.Should().Be(26.0m, "(2,5 + 0 + 1,3 + 100) / 4 = 25,95 → 26,0");
    }

    [Fact]
    public async Task Students_Fall_In_One_And_Only_One_Progress_Band()
    {
        var d = await DashboardAsync(EcoleA);

        d.Bands.Select(b => (b.Min, b.Max)).Should().Equal(ProgressDashboardRules.Bands);
        d.Bands.Select(b => b.StudentCount).Should().Equal(1, 2, 0, 0, 0, 1);   // 0 % | ≤10 % (Awa, Cheikh) | … | 100 % (Demba)
        d.Bands.Sum(b => b.StudentCount).Should().Be(d.StudentsInHalqa, "chaque élève dans une tranche, une seule");
    }

    [Fact]
    public async Task Stagnation_Flags_Never_Evaluated_And_Stale_Students_But_Not_Recent_Or_Finished_Ones()
    {
        var d = await DashboardAsync(EcoleA, staleDays: 30);

        d.StagnantCount.Should().Be(2);
        d.Stagnant.Select(s => s.StudentId).Should().Equal(
            new[] { Jamais, Ancien }, "jamais évalué d'abord, puis du plus ancien au plus récent");

        d.Stagnant[0].LastEvaluatedAt.Should().BeNull();
        d.Stagnant[0].DaysSinceEvaluation.Should().BeNull("jamais évalué : pas de nombre de jours inventé");
        d.Stagnant[1].DaysSinceEvaluation.Should().Be(60);
        d.Stagnant[1].InstructorName.Should().Be("Oustaz Fall");
        d.Stagnant.Should().NotContain(s => s.StudentId == Recent, "évalué il y a 5 jours");
        d.Stagnant.Should().NotContain(s => s.StudentId == Termine, "un élève qui a fini le Coran n'est jamais relancé");
        d.Stagnant.Should().NotContain(s => s.StudentId == SansHalqa);
    }

    [Theory]
    [InlineData(90, 1)]    // 60 jours < 90 : seul l'élève jamais évalué reste signalé
    [InlineData(3, 3)]     // 5 jours > 3 : Awa est signalée aussi
    public async Task The_Stale_Threshold_Is_Configurable(int staleDays, int expectedCount)
    {
        (await DashboardAsync(EcoleA, staleDays)).StagnantCount.Should().Be(expectedCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    [InlineData(100000, 365)]
    public async Task The_Stale_Threshold_Is_Clamped(int requested, int effective)
    {
        (await DashboardAsync(EcoleA, requested)).StaleDays.Should().Be(effective);
    }

    [Fact]
    public async Task The_Halqa_Summary_Shows_Size_Average_Completed_Hizbs_And_Stagnation_Per_Oustaz()
    {
        var d = await DashboardAsync(EcoleA);

        d.Halqas.Select(h => h.InstructorName).Should().Equal("Oustaz Fall", "Serigne Modou");

        var modou = d.Halqas.Single(h => h.InstructorId == Oustaz1);
        modou.StudentCount.Should().Be(2);
        modou.AverageProgressPercent.Should().Be(1.3m, "(2,5 + 0) / 2 = 1,25 → 1,3");
        modou.CompletedHizbs.Should().Be(1);
        modou.StagnantCount.Should().Be(1);
        modou.InstructorNameAr.Should().Be("سيرين مودو");

        var fall = d.Halqas.Single(h => h.InstructorId == Oustaz2);
        fall.StudentCount.Should().Be(2);
        fall.CompletedHizbs.Should().Be(60);
        fall.StagnantCount.Should().Be(1);
    }

    [Fact]
    public async Task The_Dashboard_Never_Mixes_Schools()
    {
        var a = await DashboardAsync(EcoleA);
        var b = await DashboardAsync(EcoleB);

        b.StudentsInHalqa.Should().Be(1);
        b.CompletedQuarters.Should().Be(0);
        b.Halqas.Should().ContainSingle().Which.InstructorId.Should().Be(OustazB);
        b.Stagnant.Should().ContainSingle().Which.StudentId.Should().Be(EleveB);
        a.Halqas.Should().NotContain(h => h.InstructorId == OustazB);
        a.Stagnant.Should().NotContain(s => s.StudentId == EleveB);
    }

    [Fact]
    public async Task A_School_Without_Any_Halqa_Gets_An_Empty_Dashboard_Not_An_Error()
    {
        var empty = Guid.Parse("d3333333-3333-3333-3333-333333333333");
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Schools.Add(new School { Id = empty, Name = "École standard" });
            await owner.SaveChangesAsync();
        }

        var d = await DashboardAsync(empty);

        d.StudentsInHalqa.Should().Be(0);
        d.AverageProgressPercent.Should().Be(0);
        d.Halqas.Should().BeEmpty();
        d.Stagnant.Should().BeEmpty();
        d.Bands.Should().OnlyContain(b => b.StudentCount == 0);
    }

    // ------------------------------------------------------------------ bulletin

    private async Task<HizbReportDto> ReportAsync(Guid userId, Role role, Guid student)
    {
        await using var db = _db.NewAppContext(EcoleA);
        var scope = new HalqaScopeAuthorizer(db, new TestCurrentUser(userId, role));
        return await new GetStudentHizbReportQueryHandler(new ChainSender(db, scope), db, new FixedTimeProvider(Now))
            .Handle(new GetStudentHizbReportQuery(student), CancellationToken.None);
    }

    [Fact]
    public async Task The_Report_Carries_Identity_Oustaz_Summary_And_The_60_Hizb()
    {
        var r = await ReportAsync(UserDirecteur, Role.Directeur, Recent);

        r.SchoolName.Should().Be("Daara Al Azhar");
        r.FullName.Should().Be("Awa Diop");
        r.FullNameAr.Should().Be("عائشة جوب");
        r.Matricule.Should().Be("ELEV-A1");
        r.ClassroomName.Should().Be("Halqa A");
        r.InstructorName.Should().Be("Serigne Modou");
        r.InstructorNameAr.Should().Be("سيرين مودو");
        r.Hizbs.Should().HaveCount(60);
        r.Summary.CompletedHizbs.Should().Be(1);
        r.Summary.CompletedQuarters.Should().Be(6);
        r.AverageRating.Should().Be(4.0m, "(5 + 3) / 2");
        r.LastEvaluatedAt.Should().Be(Now.AddDays(-5));
    }

    [Fact]
    public async Task A_Student_Never_Evaluated_Has_No_Average_And_No_Evaluation_Date()
    {
        var r = await ReportAsync(UserDirecteur, Role.Directeur, Jamais);

        r.AverageRating.Should().BeNull("aucune note n'est inventée");
        r.LastEvaluatedAt.Should().BeNull();
        r.Summary.CompletedQuarters.Should().Be(0);
    }

    [Fact]
    public async Task An_Oustaz_Gets_The_Report_Of_His_Own_Students_Only()
    {
        (await ReportAsync(UserOustaz1, Role.Enseignant, Recent)).StudentId.Should().Be(Recent);

        var other = async () => await ReportAsync(UserOustaz1, Role.Enseignant, Ancien);
        await other.Should().ThrowAsync<ForbiddenException>();

        var unassigned = async () => await ReportAsync(UserOustaz1, Role.Enseignant, SansHalqa);
        await unassigned.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task The_Report_Of_A_Student_Of_Another_School_Is_Not_Found()
    {
        var act = async () => await ReportAsync(UserDirecteur, Role.Directeur, EleveB);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task The_Pdf_Is_A_Real_Pdf_Even_With_Arabic_Names()
    {
        PdfFonts.EnsureRegistered();
        await using var db = _db.NewAppContext(EcoleA);
        var scope = new HalqaScopeAuthorizer(db, new TestCurrentUser(UserDirecteur, Role.Directeur));
        var handler = new GetStudentHizbReportPdfQueryHandler(new ChainSender(db, scope), new HizbReportPdfGenerator(), new NoLogoProvider());

        // Le Handler du PDF n'enchaîne que sur GetStudentHizbReportQuery, que ChainSender route vers le vrai Handler.
        var result = await handler.Handle(new GetStudentHizbReportPdfQuery(Recent), CancellationToken.None);

        result.Matricule.Should().Be("ELEV-A1");
        result.Content.Length.Should().BeGreaterThan(3000);
        Encoding.ASCII.GetString(result.Content, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public async Task The_Pdf_Of_A_Student_Outside_The_Oustaz_Halqa_Is_Refused_Before_Any_Rendering()
    {
        PdfFonts.EnsureRegistered();
        await using var db = _db.NewAppContext(EcoleA);
        var scope = new HalqaScopeAuthorizer(db, new TestCurrentUser(UserOustaz1, Role.Enseignant));
        var handler = new GetStudentHizbReportPdfQueryHandler(new ChainSender(db, scope), new HizbReportPdfGenerator(), new NoLogoProvider());

        var act = async () => await handler.Handle(new GetStudentHizbReportPdfQuery(Ancien), CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }
}
