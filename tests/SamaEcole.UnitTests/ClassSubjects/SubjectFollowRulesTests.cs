using FluentAssertions;
using SamaEcole.Application.ClassSubjects;
using SamaEcole.Application.Coefficients;
using SamaEcole.Domain.Entities;
using Xunit;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.UnitTests.ClassSubjects;

/// <summary>
/// Évolution N°6 — qui suit quelle matière, et comment un choix d'options se résout. Règles pures : le calcul du
/// bulletin et la grille de saisie en dépendent.
/// </summary>
public class SubjectFollowRulesTests
{
    private static readonly Guid Maths = Guid.NewGuid(), Svt = Guid.NewGuid(), Pc = Guid.NewGuid(), Info = Guid.NewGuid();
    private static readonly Guid CsMaths = Guid.NewGuid(), CsSvt = Guid.NewGuid(), CsPc = Guid.NewGuid(), CsInfo = Guid.NewGuid();

    private static readonly ClassSubjectRule[] L2 =
    [
        new(CsMaths, Maths, IsActive: true, OptionGroup: null),
        new(CsSvt, Svt, IsActive: true, OptionGroup: "Option scientifique"),
        new(CsPc, Pc, IsActive: true, OptionGroup: "Option scientifique"),
        new(CsInfo, Info, IsActive: false, OptionGroup: null)
    ];

    [Fact]
    public void A_Student_Who_Chose_Svt_Does_Not_Follow_Pc_Nor_A_Deactivated_Subject()
        => SubjectFollowRules.ExcludedSubjects(L2, new HashSet<Guid> { CsSvt })
            .Should().BeEquivalentTo([Pc, Info]);

    [Fact]
    public void A_Student_Without_A_Choice_Follows_No_Option_Of_The_Group()
        => SubjectFollowRules.ExcludedSubjects(L2, new HashSet<Guid>())
            .Should().BeEquivalentTo([Svt, Pc, Info]);

    [Fact]
    public void A_Class_Without_Programme_Excludes_Nothing()
        => SubjectFollowRules.ExcludedSubjects([], new HashSet<Guid>()).Should().BeEmpty();

    [Theory]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    [InlineData(" LV2 ", "LV2")]
    public void A_Group_Name_Is_Trimmed_And_Blank_Means_No_Group(string? input, string? expected)
        => SubjectFollowRules.NormalizeGroup(input).Should().Be(expected);

    // ── Résolution d'un choix (StudentOptionWriter.Resolve) ─────────────────────────────────────────

    private static readonly Guid Esp = Guid.NewGuid(), All = Guid.NewGuid();

    private static readonly OptionGroupDto[] Groups =
    [
        new("Option scientifique", [new(CsSvt, Svt, "SVT"), new(CsPc, Pc, "Physique-Chimie")], CsSvt, null),
        new("LV2", [new(Esp, Guid.NewGuid(), "Espagnol"), new(All, Guid.NewGuid(), "Allemand")], Esp, null)
    ];

    [Fact]
    public void Missing_Groups_Receive_Their_Default_Option_When_Asked()
        => StudentOptionWriter.Resolve(Groups, [CsPc], fillDefaults: true, "f").Should().Equal(CsPc, Esp);

    [Fact]
    public void Missing_Groups_Stay_Empty_Otherwise()
        => StudentOptionWriter.Resolve(Groups, [All], fillDefaults: false, "f").Should().Equal(All);

    [Fact]
    public void Two_Options_Of_The_Same_Group_Are_Refused()
    {
        var act = () => StudentOptionWriter.Resolve(Groups, [CsSvt, CsPc], fillDefaults: true, "f");
        act.Should().Throw<ValidationException>();
    }

    [Fact]
    public void An_Option_Outside_The_Class_Is_Refused()
    {
        var act = () => StudentOptionWriter.Resolve(Groups, [Guid.NewGuid()], fillDefaults: true, "f");
        act.Should().Throw<ValidationException>();
    }

    // ── Choix de la matière de l'établissement qui porte une ligne du modèle ──────────────────────────

    private static Subject NewSubject(string name, string level) =>
        new() { SchoolId = Guid.NewGuid(), Name = name, Level = level, Coefficient = 1 };

    [Fact]
    public void The_Subject_At_The_Level_Of_The_Class_Wins_Then_The_Lycee_Then_A_Free_Level()
    {
        var college = NewSubject("Maths", "Collège");
        var secondaire = NewSubject("Mathématiques", "Secondaire");
        var lycee = NewSubject("Mathématiques", "Lycée");
        var terminale = NewSubject("Mathématiques", "Terminale S2");
        var line = SeriesCoefficientTemplates.For("S2").Single(l => l.Label == "Mathématiques");

        ClassSubjectTemplateInjector.Pick(line, [college, secondaire, lycee, terminale], "Lycée").Should().BeSameAs(lycee);
        ClassSubjectTemplateInjector.Pick(line, [college, secondaire, terminale], "Lycée").Should().BeSameAs(secondaire);
        ClassSubjectTemplateInjector.Pick(line, [college, terminale], "Lycée").Should().BeSameAs(terminale);
        ClassSubjectTemplateInjector.Pick(line, [NewSubject("Informatique", "Lycée")], "Lycée").Should().BeNull();
    }

    [Theory]
    [InlineData("Collège")]
    [InlineData("CEM")]
    [InlineData("Moyen")]
    public void A_College_Subject_Is_Never_Bound_To_A_Lycee_Class(string level)
    {
        var line = SeriesCoefficientTemplates.For("S2").Single(l => l.Label == "Mathématiques");

        ClassSubjectTemplateInjector.Pick(line, [NewSubject("Maths", level)], "Lycée").Should().BeNull();
    }
}
