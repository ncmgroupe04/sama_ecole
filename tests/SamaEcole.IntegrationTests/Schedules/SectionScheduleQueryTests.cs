using FluentAssertions;
using SamaEcole.Application.Features.Schedules;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using SamaEcole.IntegrationTests.Common;
using Xunit;

namespace SamaEcole.IntegrationTests.Schedules;

/// <summary>
/// Alternance français / arabe lue sur de VRAIS créneaux en base : la section d'un créneau vient du pôle de sa
/// matière, et l'isolation d'école tient (un créneau d'une autre école n'entre jamais dans l'analyse).
/// </summary>
[Trait("Category", "MultiTenant")]
public class SectionScheduleQueryTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    private static readonly Guid Ecole = Guid.Parse("99111111-1111-1111-1111-111111111111");
    private static readonly Guid AutreEcole = Guid.Parse("99111111-1111-1111-1111-111111111112");
    private static readonly Guid Classe = Guid.Parse("99111111-0000-0000-0000-0000000000c1");
    private static readonly Guid ClasseAutre = Guid.Parse("99111111-0000-0000-0000-0000000000c2");
    private static readonly Guid Maths = Guid.Parse("99111111-0000-0000-0000-0000000000a1");
    private static readonly Guid Fiqh = Guid.Parse("99111111-0000-0000-0000-0000000000a2");
    private static readonly Guid LangueArabe = Guid.Parse("99111111-0000-0000-0000-0000000000a3");
    private static readonly Guid Eps = Guid.Parse("99111111-0000-0000-0000-0000000000a4");
    private static readonly Guid MathsAutre = Guid.Parse("99111111-0000-0000-0000-0000000000a5");
    private static readonly Guid ProfBilingue = Guid.Parse("99111111-0000-0000-0000-0000000000b1");
    private static readonly Guid ProfFrancais = Guid.Parse("99111111-0000-0000-0000-0000000000b2");
    private static readonly Guid ProfAutre = Guid.Parse("99111111-0000-0000-0000-0000000000b3");

    public async Task InitializeAsync()
    {
        await _db.InitializeAsync();
        await using var owner = _db.NewOwnerContext();

        owner.Schools.AddRange(new School { Id = Ecole, Name = "École A" }, new School { Id = AutreEcole, Name = "École B" });
        owner.Classrooms.AddRange(
            new Classroom { Id = Classe, SchoolId = Ecole, Name = "5ème A", Level = "Collège", Capacity = 40 },
            new Classroom { Id = ClasseAutre, SchoolId = AutreEcole, Name = "5ème B", Level = "Collège", Capacity = 40 });

        owner.Subjects.AddRange(
            Subject(Maths, Ecole, "Mathématiques", SectionType.French),
            Subject(Fiqh, Ecole, "Fiqh", SectionType.IslamicStudies),
            Subject(LangueArabe, Ecole, "Langue arabe", SectionType.Arabic),
            Subject(Eps, Ecole, "EPS", SectionType.Common),
            Subject(MathsAutre, AutreEcole, "Mathématiques", SectionType.French));

        owner.Teachers.AddRange(
            Teacher(ProfBilingue, Ecole, "ENS-1", "Oumar Ba"),
            Teacher(ProfFrancais, Ecole, "ENS-2", "Awa Fall"),
            Teacher(ProfAutre, AutreEcole, "ENS-3", "Autre Prof"));

        // Classe 5ème A : matin français (maths + EPS commun), après-midi arabe (arabe + sciences islamiques),
        // lundi et mardi — « matin français / après-midi arabe » chaque jour.
        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday })
        {
            owner.ScheduleSlots.AddRange(
                Slot(Ecole, ProfFrancais, Classe, Maths, day, 8, 10),
                Slot(Ecole, ProfFrancais, Classe, Eps, day, 10, 11),
                Slot(Ecole, ProfBilingue, Classe, LangueArabe, day, 14, 16),
                Slot(Ecole, ProfBilingue, Classe, Fiqh, day, 16, 17));
        }

        // Un créneau d'une AUTRE école, qui ne doit jamais apparaître.
        owner.ScheduleSlots.Add(Slot(AutreEcole, ProfAutre, ClasseAutre, MathsAutre, DayOfWeek.Monday, 8, 12));

        await owner.SaveChangesAsync(CancellationToken.None);
    }

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    private async Task<SectionScheduleDto> ClassroomAsync(Guid classroom, Guid school)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new GetSectionScheduleQueryHandlers(ctx)
            .Handle(new GetClassroomSectionScheduleQuery(classroom), CancellationToken.None);
    }

    private async Task<SectionScheduleDto> TeacherAsync(Guid teacher, Guid school)
    {
        await using var ctx = _db.NewAppContext(school);
        return await new GetSectionScheduleQueryHandlers(ctx)
            .Handle(new GetTeacherSectionScheduleQuery(teacher), CancellationToken.None);
    }

    [Fact]
    public async Task A_Class_Shows_Morning_French_And_Afternoon_Arabic_From_The_Poles_Of_Its_Subjects()
    {
        var result = await ClassroomAsync(Classe, Ecole);

        result.Pattern.Should().Be(SectionPattern.MorningFrenchAfternoonArabic);
        result.FrenchHours.Should().Be(6, "maths (2 h) + EPS commun (1 h), deux jours");
        result.ArabicHours.Should().Be(6, "langue arabe (2 h) + sciences islamiques (1 h), deux jours");
        result.CoversBothSections.Should().BeTrue();
        result.Notices.Should().BeEmpty();
    }

    [Fact]
    public async Task A_Bilingual_Teacher_Is_Told_Apart_From_A_Single_Section_One()
    {
        var bilingue = await TeacherAsync(ProfBilingue, Ecole);
        var francais = await TeacherAsync(ProfFrancais, Ecole);

        // Le « bilingue » enseigne l'arabe ET les sciences islamiques : ce sont deux matières, mais UNE section.
        bilingue.CoversBothSections.Should().BeFalse();
        bilingue.Pattern.Should().Be(SectionPattern.SingleSection);
        bilingue.ArabicHours.Should().Be(6);
        francais.Pattern.Should().Be(SectionPattern.SingleSection);
        francais.FrenchHours.Should().Be(6);
    }

    [Fact]
    public async Task A_Teacher_Covering_Both_Sections_Is_Flagged_As_Bilingual()
    {
        await using (var owner = _db.NewOwnerContext())
        {
            owner.ScheduleSlots.Add(Slot(Ecole, ProfFrancais, Classe, Fiqh, DayOfWeek.Wednesday, 8, 9));
            await owner.SaveChangesAsync(CancellationToken.None);
        }

        var result = await TeacherAsync(ProfFrancais, Ecole);

        result.CoversBothSections.Should().BeTrue();
        result.FrenchHours.Should().Be(6);
        result.ArabicHours.Should().Be(1);
    }

    [Fact]
    public async Task A_Slot_Of_Another_School_Never_Enters_The_Analysis()
    {
        var result = await ClassroomAsync(ClasseAutre, Ecole);

        result.Pattern.Should().Be(SectionPattern.NoSlots, "la classe de l'autre école est invisible depuis cette école (RLS + filtre)");
        result.FrenchHours.Should().Be(0);
    }

    [Fact]
    public async Task A_Teacher_Of_Another_School_Is_Invisible_Too()
    {
        var result = await TeacherAsync(ProfAutre, Ecole);

        result.Pattern.Should().Be(SectionPattern.NoSlots);
    }

    [Fact]
    public async Task A_Class_Without_Any_Slot_Has_No_Pattern()
    {
        var result = await ClassroomAsync(Guid.NewGuid(), Ecole);

        result.Pattern.Should().Be(SectionPattern.NoSlots);
        result.HalfDays.Should().BeEmpty();
    }

    private static Subject Subject(Guid id, Guid school, string name, SectionType pole) =>
        new() { Id = id, SchoolId = school, Name = name, Level = "Collège", Coefficient = 2, SectionType = pole };

    private static Teacher Teacher(Guid id, Guid school, string matricule, string name) => new()
    {
        Id = id, SchoolId = school, Matricule = matricule, FullName = name,
        Email = $"{matricule.ToLowerInvariant()}@ecole.sn", BirthDate = new DateOnly(1985, 1, 1)
    };

    private static ScheduleSlot Slot(Guid school, Guid teacher, Guid classroom, Guid subject, DayOfWeek day, int start, int end) => new()
    {
        SchoolId = school, TeacherId = teacher, ClassroomId = classroom, SubjectId = subject,
        DayOfWeek = day, StartTime = new TimeOnly(start, 0), EndTime = new TimeOnly(end, 0)
    };
}
