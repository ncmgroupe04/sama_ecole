using SamaEcole.Application.Grades;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Grades;

/// <summary>
/// Évolution N°1 — qui peut corriger une note déjà saisie. Le Directeur et le Secrétariat corrigent
/// toujours, sans limite de délai. L'Enseignant ne corrige que dans la fenêtre GradeEditWindowDays ET
/// s'il est l'auteur de la note OU affecté à la classe/matière — jamais la note d'un collègue d'une
/// autre matière.
/// </summary>
public class GradeEditPolicyTests
{
    private static readonly DateTimeOffset Created = new(2026, 9, 1, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(Role.Directeur)]
    [InlineData(Role.Secretariat)]
    [InlineData(Role.SuperAdmin)]
    public void Elevated_Roles_Correct_At_Any_Age_Even_Without_Ownership(Role role)
    {
        var yearLater = Created.AddDays(400);

        GradeEditPolicy.CanCorrect(role, Created, yearLater, windowDays: 7, isAuthor: false, isAssigned: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Enseignant_Author_Inside_The_Window_Can_Correct()
    {
        GradeEditPolicy.CanCorrect(Role.Enseignant, Created, Created.AddDays(3), 7, isAuthor: true, isAssigned: false)
            .Should().BeTrue();
    }

    [Fact]
    public void Enseignant_Assigned_But_Not_Author_Inside_The_Window_Can_Correct()
    {
        GradeEditPolicy.CanCorrect(Role.Enseignant, Created, Created.AddDays(3), 7, isAuthor: false, isAssigned: true)
            .Should().BeTrue();
    }

    [Fact]
    public void Enseignant_Neither_Author_Nor_Assigned_Cannot_Correct_A_Colleagues_Grade()
    {
        GradeEditPolicy.CanCorrect(Role.Enseignant, Created, Created.AddDays(1), 7, isAuthor: false, isAssigned: false)
            .Should().BeFalse();
    }

    [Fact]
    public void Enseignant_Author_Past_The_Window_Cannot_Correct()
    {
        GradeEditPolicy.CanCorrect(Role.Enseignant, Created, Created.AddDays(7).AddMinutes(1), 7, isAuthor: true, isAssigned: true)
            .Should().BeFalse();
    }

    [Fact]
    public void The_Window_Boundary_Is_Inclusive()
    {
        // « le délai écoulé ne dépasse pas GradeEditWindowDays » : exactement 7 jours passe encore.
        GradeEditPolicy.CanCorrect(Role.Enseignant, Created, Created.AddDays(7), 7, isAuthor: true, isAssigned: false)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(Role.Finance)]
    [InlineData(Role.Surveillant)]
    public void Other_Roles_Never_Correct(Role role)
    {
        GradeEditPolicy.CanCorrect(role, Created, Created, 7, isAuthor: true, isAssigned: true)
            .Should().BeFalse();
    }

    [Fact]
    public void A_Missing_Role_Never_Corrects()
    {
        GradeEditPolicy.CanCorrect(null, Created, Created, 7, isAuthor: true, isAssigned: true)
            .Should().BeFalse();
    }
}
