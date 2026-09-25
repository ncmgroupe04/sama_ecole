using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Application.Enrollments.Commands.SetEnrollmentOptions;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentOptions;
using SamaEcole.Application.OptionalSubjects;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using SamaEcole.Persistence;
using Xunit;

namespace SamaEcole.IntegrationTests.OptionalSubjects;

/// <summary>Cache KPI désactivé : ces tests exercent le Handler directement, hors DI (comme EnrollmentTests).</summary>
file sealed class NoOpKpiCacheService : IKpiCacheService
{
    public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory, CancellationToken cancellationToken) =>
        factory(cancellationToken);

    public void Invalidate(string key) { }
}

/// <summary>
/// Options et dispenses d'une inscription : validation (un choix par groupe, un motif pour une matière
/// obligatoire), remplacement idempotent de CHAQUE moitié indépendamment, lecture, et écriture des options
/// dans la même transaction que l'inscription.
/// </summary>
[Trait("Category", "MultiTenant")]
public class EnrollmentOptionsTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("98888888-8888-8888-8888-888888888888");
    private static readonly Guid Autre = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private static readonly Guid Annee = Guid.Parse("98888888-0000-0000-0000-000000000001");
    private static readonly Guid AnneePassee = Guid.Parse("98888888-0000-0000-0000-000000000002");
    private static readonly Guid Trimestre = Guid.Parse("98888888-0000-0000-0000-0000000000d1");
    private static readonly Guid Classe = Guid.Parse("98888888-0000-0000-0000-0000000000c1");
    private static readonly Guid Eleve = Guid.Parse("98888888-0000-0000-0000-0000000000e1");
    private static readonly Guid Inscription = Guid.Parse("98888888-0000-0000-0000-0000000000f1");
    private static readonly Guid InscriptionPassee = Guid.Parse("98888888-0000-0000-0000-0000000000f2");
    private static readonly Guid CatInscription = Guid.Parse("98888888-0000-0000-0000-0000000000b1");
    private static readonly Guid Directeur = Guid.Parse("98888888-0000-0000-0000-0000000000d0");

    private static readonly Guid Maths = Guid.Parse("98888888-0000-0000-0000-0000000000a0");
    private static readonly Guid Eps = Guid.Parse("98888888-0000-0000-0000-0000000000a7");
    private static readonly Guid Espagnol = Guid.Parse("98888888-0000-0000-0000-0000000000a1");
    private static readonly Guid Arabe = Guid.Parse("98888888-0000-0000-0000-0000000000a2");
    private static readonly Guid Allemand = Guid.Parse("98888888-0000-0000-0000-0000000000a3");
    private static readonly Guid Pc = Guid.Parse("98888888-0000-0000-0000-0000000000a4");
    private static readonly Guid Svt = Guid.Parse("98888888-0000-0000-0000-0000000000a5");
    private static readonly Guid Dessin = Guid.Parse("98888888-0000-0000-0000-0000000000a6");
    // Ajouts au jeu du plan : une option d'un AUTRE niveau, et un domaine (matière parente d'une activité).
    private static readonly Guid LatinLycee = Guid.Parse("98888888-0000-0000-0000-0000000000a8");
    private static readonly Guid Domaine = Guid.Parse("98888888-0000-0000-0000-0000000000a9");
    private static readonly Guid Activite = Guid.Parse("98888888-0000-0000-0000-0000000000aa");

    private sealed class StubTenantProvider(Guid? schoolId) : ITenantProvider
    {
        public Guid? CurrentSchoolId => schoolId;
    }

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "Collège A" }, new School { Id = Autre, Name = "Collège B" });
        owner.SchoolYears.AddRange(
            new SchoolYear { Id = Annee, SchoolId = Ecole, Label = "2026-2027", StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2027, 6, 30), IsActive = true },
            new SchoolYear { Id = AnneePassee, SchoolId = Ecole, Label = "2025-2026", StartDate = new DateOnly(2025, 9, 1), EndDate = new DateOnly(2026, 6, 30) });
        owner.Terms.Add(new Term
        {
            Id = Trimestre, SchoolId = Ecole, SchoolYearId = Annee, Label = "1er trimestre", Order = 1,
            StartDate = new DateOnly(2026, 9, 1), EndDate = new DateOnly(2026, 12, 20)
        });
        owner.Classrooms.Add(new Classroom { Id = Classe, SchoolId = Ecole, Name = "4ème A", Level = "Collège", Cycle = CycleType.College, Capacity = 40 });

        owner.FeeCategories.Add(new FeeCategory { Id = CatInscription, SchoolId = Ecole, Name = "Inscription", IsRecurring = false });
        owner.ClassFees.Add(new ClassFee { SchoolId = Ecole, FeeCategoryId = CatInscription, ClassroomId = Classe, Amount = 10_000m });

        owner.Subjects.AddRange(
            new Subject { Id = Maths, SchoolId = Ecole, Name = "Mathématiques", Level = "Collège", Coefficient = 4 },
            new Subject { Id = Eps, SchoolId = Ecole, Name = "EPS", Level = "Collège", Coefficient = 1 },
            Option(Espagnol, "Espagnol", "LV2"), Option(Arabe, "Arabe", "LV2"), Option(Allemand, "Allemand", "LV2"),
            Option(Pc, "PC", "Option scientifique"), Option(Svt, "SVT", "Option scientifique"),
            Option(Dessin, "Dessin", null),
            new Subject { Id = LatinLycee, SchoolId = Ecole, Name = "Latin", Level = "Lycée", Coefficient = 2, IsOptional = true },
            new Subject { Id = Domaine, SchoolId = Ecole, Name = "Lang & Com.", Level = "Collège", Coefficient = 3 },
            new Subject { Id = Activite, SchoolId = Ecole, Name = "Vocabulaire", Level = "Collège", Coefficient = 1, ParentSubjectId = Domaine });

        owner.Students.Add(new Student
        {
            Id = Eleve, SchoolId = Ecole, Matricule = "ELEV-0001", FullName = "Awa Fall",
            BirthDate = new DateOnly(2011, 1, 1), BirthPlace = "Dakar", Gender = "F", ClassroomId = Classe
        });
        owner.Enrollments.AddRange(
            NewEnrollment(Inscription, Annee, "R-1"), NewEnrollment(InscriptionPassee, AnneePassee, "R-0"));
        owner.Grades.AddRange(
            new Grade { SchoolId = Ecole, StudentId = Eleve, SubjectId = Arabe, TermId = Trimestre, EvaluationType = EvaluationType.Composition, Value = 10 },
            new Grade { SchoolId = Ecole, StudentId = Eleve, SubjectId = Eps, TermId = Trimestre, EvaluationType = EvaluationType.Composition, Value = 8 });

        await owner.SaveChangesAsync();
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private static Subject Option(Guid id, string name, string? group) => new()
    {
        Id = id, SchoolId = Ecole, Name = name, Level = "Collège", Coefficient = 2, IsOptional = true, OptionGroup = group
    };

    private static Enrollment NewEnrollment(Guid id, Guid yearId, string receipt) => new()
    {
        Id = id, SchoolId = Ecole, StudentId = Eleve, SchoolYearId = yearId, ClassroomId = Classe,
        Type = EnrollmentType.NewEnrollment, Status = EnrollmentStatus.Confirmed, ReceiptNumber = receipt
    };

    private async Task SendAsync(Guid enrollmentId, IReadOnlyList<Guid>? options, IReadOnlyList<MandatoryExemption>? exemptions)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var handler = new SetEnrollmentOptionsCommandHandler(
            ctx, new StubTenantProvider(Ecole), new TestCurrentUser(Directeur, Role.Directeur));

        await handler.Handle(new SetEnrollmentOptionsCommand(enrollmentId, options, exemptions), default);
    }

    /// <summary>Choix d'options seul (les dispenses de matières obligatoires ne sont pas touchées).</summary>
    private Task SetAsync(params Guid[] optionIds) => SendAsync(Inscription, optionIds, null);

    /// <summary>Dispenses de matières obligatoires seules (le choix d'options n'est pas touché).</summary>
    private Task SetExemptionsAsync(params MandatoryExemption[] exemptions) => SendAsync(Inscription, null, exemptions);

    private async Task<List<(Guid SubjectId, string? Reason)>> RowsAsync(Guid? enrollmentId = null)
    {
        await using var ctx = _db.NewAppContext(Ecole);
        var id = enrollmentId ?? Inscription;
        var rows = await ctx.EnrollmentSubjectExemptions.AsNoTracking()
            .Where(x => x.EnrollmentId == id)
            .Select(x => new { x.SubjectId, x.Reason })
            .ToListAsync();
        return rows.Select(r => (r.SubjectId, r.Reason)).ToList();
    }

    private async Task<List<Guid>> ExemptedAsync(Guid? enrollmentId = null) =>
        (await RowsAsync(enrollmentId)).Select(r => r.SubjectId).ToList();

    private async Task<EnrollmentOptionsDto> QueryAsync()
    {
        await using var ctx = _db.NewAppContext(Ecole);
        return await new GetEnrollmentOptionsQueryHandler(ctx)
            .Handle(new GetEnrollmentOptionsQuery(Inscription), default);
    }

    // ---- Options -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Choosing_One_Subject_Per_Group_Exempts_The_Others_And_Is_Idempotent()
    {
        await SetAsync(Espagnol, Pc, Dessin);
        await SetAsync(Espagnol, Pc, Dessin); // 2e appel identique : aucun doublon, aucune erreur

        (await ExemptedAsync()).Should().BeEquivalentTo([Arabe, Allemand, Svt]);
    }

    [Fact]
    public async Task Changing_The_Choice_Soft_Deletes_The_Old_Exemptions_And_Adds_The_New_Ones()
    {
        await SetAsync(Espagnol, Pc, Dessin);
        await SetAsync(Arabe, Svt, Dessin);

        (await ExemptedAsync()).Should().BeEquivalentTo([Espagnol, Allemand, Pc]);

        await using var owner = _db.NewOwnerContext();
        (await owner.EnrollmentSubjectExemptions.IgnoreQueryFilters()
            .CountAsync(x => x.EnrollmentId == Inscription && x.IsDeleted))
            .Should().BeGreaterThan(0, "les lignes retirées sont supprimées logiquement, jamais physiquement (règle #6)");
    }

    [Fact]
    public async Task An_Empty_Choice_Exempts_Every_Option_Of_The_Level()
    {
        await SetAsync();

        (await ExemptedAsync()).Should().BeEquivalentTo([Espagnol, Arabe, Allemand, Pc, Svt, Dessin]);
    }

    [Fact]
    public async Task Two_Subjects_Of_The_Same_Group_Are_Refused()
    {
        var act = () => SetAsync(Espagnol, Arabe);

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors;
        errors.Should().ContainKey("SubjectIds", "l'erreur est portée par le champ « subjectIds »");
        errors["SubjectIds"].Should().Contain(m => m.Contains("LV2"));
        (await ExemptedAsync()).Should().BeEmpty("un choix refusé n'écrit rien");
    }

    [Fact]
    public async Task A_Mandatory_Subject_Is_Not_An_Option_And_Is_Refused()
    {
        var act = () => SetAsync(Maths);

        await act.Should().ThrowAsync<ValidationException>();
    }

    // Ajout au plan : appariement de niveau de LoadLevelOptionsAsync (casse/espaces tolérés, autre niveau exclu).
    [Fact]
    public async Task Options_Are_Matched_By_Level_Ignoring_Case_And_Edge_Spaces()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.Subjects.Add(new Subject
            {
                Id = Guid.Parse("98888888-0000-0000-0000-0000000000ab"), SchoolId = Ecole, Name = "Musique",
                Level = " collège ", Coefficient = 1, IsOptional = true
            });
            await owner.SaveChangesAsync();
        }

        var act = () => SetAsync(LatinLycee);
        await act.Should().ThrowAsync<ValidationException>("une option d'un autre niveau n'est pas choisissable");

        await SetAsync();

        (await ExemptedAsync()).Should().BeEquivalentTo(
            [Espagnol, Arabe, Allemand, Pc, Svt, Dessin, Guid.Parse("98888888-0000-0000-0000-0000000000ab")],
            "Musique (« collège ») est du niveau « Collège » ; Latin (Lycée) ne l'est pas");
    }

    // ---- Dispenses de matières obligatoires ---------------------------------------------------------------

    [Fact]
    public async Task A_Mandatory_Exemption_Is_Recorded_With_Its_Trimmed_Reason()
    {
        await SetExemptionsAsync(new MandatoryExemption(Eps, "  Inaptitude médicale  "));

        (await RowsAsync()).Should().Equal([(Eps, (string?)"Inaptitude médicale")]);
    }

    [Fact]
    public async Task A_Mandatory_Exemption_Without_A_Reason_Is_Refused_And_Writes_Nothing()
    {
        var act = () => SetExemptionsAsync(new MandatoryExemption(Eps, "   "));

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors;
        errors.Should().ContainKey("Exemptions", "l'erreur est portée par le champ « exemptions »");
        errors["Exemptions"].Should().Contain(m => m.Contains("motif"));
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task An_Option_Cannot_Be_Passed_As_A_Mandatory_Exemption()
    {
        var act = () => SetExemptionsAsync(new MandatoryExemption(Arabe, "Raison"));

        await act.Should().ThrowAsync<ValidationException>();
    }

    // Ajout au plan : filtre « matière obligatoire AUTONOME » de LoadLevelMandatoryAsync.
    [Fact]
    public async Task A_Domain_Or_An_Activity_Cannot_Be_Passed_As_A_Mandatory_Exemption()
    {
        var domain = () => SetExemptionsAsync(new MandatoryExemption(Domaine, "Raison"));
        var activity = () => SetExemptionsAsync(new MandatoryExemption(Activite, "Raison"));

        await domain.Should().ThrowAsync<ValidationException>("un domaine n'est jamais noté");
        await activity.Should().ThrowAsync<ValidationException>("une activité n'est pas une matière autonome");
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Changing_The_Reason_Updates_The_Row_Without_A_Duplicate()
    {
        await SetExemptionsAsync(new MandatoryExemption(Eps, "Certificat 2026"));
        await SetExemptionsAsync(new MandatoryExemption(Eps, "Certificat 2026-2027"));

        (await RowsAsync()).Should().Equal([(Eps, (string?)"Certificat 2026-2027")]);
    }

    [Fact]
    public async Task An_Empty_Exemption_List_Removes_The_Mandatory_Exemptions()
    {
        await SetExemptionsAsync(new MandatoryExemption(Eps, "Certificat"));
        await SendAsync(Inscription, null, []);

        (await RowsAsync()).Should().BeEmpty();
    }

    // ---- Chaque moitié est indépendante (écart E5) --------------------------------------------------------

    [Fact]
    public async Task Saving_Only_The_Options_Leaves_The_Mandatory_Exemptions_Untouched()
    {
        await SetExemptionsAsync(new MandatoryExemption(Eps, "Certificat"));

        await SetAsync(Espagnol, Pc, Dessin);

        (await RowsAsync()).Should().BeEquivalentTo(new (Guid, string?)[]
        {
            (Eps, "Certificat"), (Arabe, null), (Allemand, null), (Svt, null)
        });
    }

    [Fact]
    public async Task Saving_Only_The_Exemptions_Leaves_The_Option_Choice_Untouched()
    {
        await SetAsync(Espagnol, Pc, Dessin);

        await SetExemptionsAsync(new MandatoryExemption(Eps, "Certificat"));

        (await RowsAsync()).Should().BeEquivalentTo(new (Guid, string?)[]
        {
            (Eps, "Certificat"), (Arabe, null), (Allemand, null), (Svt, null)
        });
    }

    [Fact]
    public async Task Sending_Neither_Half_Changes_Nothing()
    {
        await SetAsync(Espagnol, Pc, Dessin);

        await SendAsync(Inscription, null, null);

        (await ExemptedAsync()).Should().BeEquivalentTo([Arabe, Allemand, Svt]);
    }

    // ---- Garde-fous ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_Cancelled_Enrollment_Cannot_Have_Its_Options_Changed()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            // Contexte propriétaire = tenant nul : le filtre global masque l'inscription, d'où IgnoreQueryFilters.
            (await owner.Enrollments.IgnoreQueryFilters().SingleAsync(e => e.Id == Inscription)).Status = EnrollmentStatus.Cancelled;
            await owner.SaveChangesAsync();
        }

        var act = () => SetAsync(Espagnol);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Only_The_Enrollment_Of_The_Active_Year_Can_Have_Its_Options_Changed()
    {
        var act = () => SendAsync(InscriptionPassee, [Espagnol], null);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task Another_School_Cannot_Reach_The_Enrollment()
    {
        await using var ctx = _db.NewAppContext(Autre);
        var handler = new SetEnrollmentOptionsCommandHandler(
            ctx, new StubTenantProvider(Autre), new TestCurrentUser(Guid.NewGuid(), Role.Directeur));

        var act = () => handler.Handle(new SetEnrollmentOptionsCommand(Inscription, [Espagnol], null), default);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    // ---- Lecture ------------------------------------------------------------------------------------------

    [Fact]
    public async Task The_Options_Query_Reports_Groups_State_And_The_Grades_A_Dispense_Would_Hide()
    {
        var before = await QueryAsync();
        before.HasExplicitChoice.Should().BeFalse("aucune ligne : l'élève suit tout");
        before.Groups.SelectMany(g => g.Subjects).Should().OnlyContain(s => s.IsFollowed);

        await SetAsync(Espagnol, Pc, Dessin);
        var after = await QueryAsync();

        after.HasExplicitChoice.Should().BeTrue();
        after.Groups.Select(g => g.Group).Should().Equal(new string?[] { "LV2", "Option scientifique", null });

        var lv2 = after.Groups.Single(g => g.Group == "LV2").Subjects;
        lv2.Single(s => s.SubjectId == Arabe).Should().Match<OptionSubjectDto>(s => !s.IsFollowed && s.GradeCount == 1);
        lv2.Single(s => s.SubjectId == Espagnol).IsFollowed.Should().BeTrue();
    }

    [Fact]
    public async Task The_Options_Query_Lists_The_Mandatory_Subjects_With_Their_Exemption_And_Reason()
    {
        await SetExemptionsAsync(new MandatoryExemption(Eps, "Inaptitude médicale"));

        var dto = await QueryAsync();

        // Ni le domaine « Lang & Com. » ni son activité ne sont listés (matières autonomes seulement).
        dto.MandatorySubjects!.Select(s => s.Name).Should().Equal("EPS", "Mathématiques");
        dto.MandatorySubjects.Single(s => s.SubjectId == Eps).Should()
            .Match<MandatorySubjectDto>(s => s.IsExempt && s.Reason == "Inaptitude médicale" && s.GradeCount == 1);
        dto.MandatorySubjects.Single(s => s.SubjectId == Maths).Should()
            .Match<MandatorySubjectDto>(s => !s.IsExempt && s.Reason == null);
        dto.HasExplicitChoice.Should().BeFalse("seule une dispense de matière obligatoire existe : aucune option n'a été choisie");
    }

    // ---- À l'inscription ----------------------------------------------------------------------------------

    private CreateEnrollmentCommandHandler NewCreateHandler(ApplicationDbContext db) =>
        new(db, new StubTenantProvider(Ecole), _db.NewGenerator(db), TimeProvider.System, new NoOpKpiCacheService());

    private static CreateEnrollmentCommand NewStudentCommand(IReadOnlyList<Guid>? options) => new()
    {
        Type = EnrollmentType.NewEnrollment,
        ClassroomId = Classe,
        FullName = "Modou Ndiaye",
        BirthDate = new DateOnly(2011, 5, 20),
        BirthPlace = "Dakar",
        Gender = "M",
        OptionSubjectIds = options
    };

    [Fact]
    public async Task Options_Chosen_At_Registration_Are_Recorded_With_The_Enrollment()
    {
        await using var db = _db.NewAppContext(Ecole);

        var receipt = await NewCreateHandler(db).Handle(NewStudentCommand([Espagnol]), default);

        (await ExemptedAsync(receipt.EnrollmentId)).Should().BeEquivalentTo([Arabe, Allemand, Pc, Svt, Dessin]);
    }

    [Fact]
    public async Task Without_A_Choice_At_Registration_No_Exemption_Is_Recorded()
    {
        await using var db = _db.NewAppContext(Ecole);

        var receipt = await NewCreateHandler(db).Handle(NewStudentCommand(null), default);

        (await ExemptedAsync(receipt.EnrollmentId)).Should().BeEmpty("aucun choix : l'élève suit toutes les options");
    }

    [Fact]
    public async Task An_Invalid_Choice_Creates_Neither_The_Student_Nor_The_Enrollment()
    {
        int enrollmentsBefore, studentsBefore;
        await using (var count = _db.NewAppContext(Ecole))
        {
            enrollmentsBefore = await count.Enrollments.CountAsync();
            studentsBefore = await count.Students.CountAsync();
        }

        await using var db = _db.NewAppContext(Ecole);
        var act = () => NewCreateHandler(db).Handle(NewStudentCommand([Espagnol, Arabe]), default);

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors;
        errors.Should().ContainKey("OptionSubjectIds");

        await using var after = _db.NewAppContext(Ecole);
        (await after.Enrollments.CountAsync()).Should().Be(enrollmentsBefore);
        (await after.Students.CountAsync()).Should().Be(studentsBefore,
            "le choix est validé avant toute écriture : ni élève ni numéro de reçu consommés");
    }
}
