using FluentAssertions;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Enrollments.Commands.CreateEnrollment;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Enrollments;

/// <summary>
/// Frais optionnels, Tâche 2 — la règle qui décide quels frais entrent dans le dû annuel d'une inscription.
///
/// Trois invariants portés ici, parce qu'ils protègent la facture :
///   1. un frais OBLIGATOIRE est toujours facturé, quoi que le client envoie (une requête forgée ne peut
///      pas décocher l'inscription ou la mensualité) ;
///   2. un frais optionnel n'est facturé que s'il est choisi — liste vide = aucun ;
///   3. l'absence de liste (client antérieur à la fonctionnalité) garde le comportement historique :
///      tous les frais de la classe sont facturés, la facture d'un ancien client ne change pas.
/// </summary>
public class OptionalFeeSelectionTests
{
    private static readonly Guid Uniforme = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid TenueSport = Guid.Parse("00000000-0000-0000-0000-0000000000a2");
    private static readonly Guid Mensualite = Guid.Parse("00000000-0000-0000-0000-0000000000b1");

    // ------------------------------------------------------------------ IsBilled

    [Fact]
    public void A_Mandatory_Fee_Is_Billed_Without_A_Selection()
    {
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, null).Should().BeTrue();
    }

    [Fact]
    public void A_Mandatory_Fee_Is_Billed_With_An_Empty_Selection()
    {
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, []).Should().BeTrue();
    }

    [Fact]
    public void A_Mandatory_Fee_Cannot_Be_Unchecked_By_A_Forged_List()
    {
        // Un client malveillant n'envoie que la tenue de sport : la mensualité reste due.
        OptionalFeeSelection.IsBilled(isOptional: false, Mensualite, [TenueSport]).Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Fee_Is_Billed_When_Chosen()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, [Uniforme]).Should().BeTrue();
    }

    [Fact]
    public void An_Optional_Fee_Is_Not_Billed_When_Not_Chosen()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, [TenueSport]).Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Selection_Bills_No_Optional_Fee()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, []).Should().BeFalse();
    }

    [Fact]
    public void Without_A_Selection_An_Optional_Fee_Keeps_The_Historical_Behaviour_And_Is_Billed()
    {
        OptionalFeeSelection.IsBilled(isOptional: true, Uniforme, null).Should().BeTrue();
    }

    // ------------------------------------------------------------------ FindInvalid

    [Fact]
    public void Without_A_Selection_Nothing_Is_Invalid()
    {
        OptionalFeeSelection.FindInvalid(null, new HashSet<Guid> { Uniforme }).Should().BeEmpty();
    }

    [Fact]
    public void Choosing_Optional_Fees_Of_The_Class_Is_Valid()
    {
        OptionalFeeSelection.FindInvalid([Uniforme, TenueSport], new HashSet<Guid> { Uniforme, TenueSport })
            .Should().BeEmpty();
    }

    [Fact]
    public void Choosing_A_Mandatory_Fee_Is_Invalid_Rather_Than_Silently_Ignored()
    {
        // La mensualité n'est pas dans les frais optionnels de la classe : la lister est une erreur de
        // saisie ou une tentative de contournement — on la signale (422), on ne l'avale pas.
        OptionalFeeSelection.FindInvalid([Mensualite], new HashSet<Guid> { Uniforme })
            .Should().Equal(Mensualite);
    }

    [Fact]
    public void Choosing_An_Unknown_Category_Is_Invalid()
    {
        var inconnue = Guid.NewGuid();

        OptionalFeeSelection.FindInvalid([Uniforme, inconnue], new HashSet<Guid> { Uniforme })
            .Should().Equal(inconnue);
    }

    // ------------------------------------------------------------------ Validator

    private readonly CreateEnrollmentCommandValidator _validator = new();

    private static CreateEnrollmentCommand ReEnrollment(IReadOnlyList<Guid>? optionalFeeCategoryIds) => new()
    {
        Type = EnrollmentType.ReEnrollment,
        ClassroomId = Guid.NewGuid(),
        StudentId = Guid.NewGuid(),
        OptionalFeeCategoryIds = optionalFeeCategoryIds
    };

    [Fact]
    public void The_Selection_Is_Absent_By_Default()
    {
        // Un ancien client n'envoie pas le champ : null, jamais une liste vide qui décocherait tout.
        new CreateEnrollmentCommand { Type = EnrollmentType.ReEnrollment, ClassroomId = Guid.NewGuid() }
            .OptionalFeeCategoryIds.Should().BeNull();
    }

    [Fact]
    public void A_Missing_Selection_Should_Pass()
    {
        _validator.Validate(ReEnrollment(null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void An_Empty_Selection_Should_Pass()
    {
        // « La famille ne prend aucun frais optionnel » est un choix légitime.
        _validator.Validate(ReEnrollment([])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Selection_Of_Distinct_Categories_Should_Pass()
    {
        _validator.Validate(ReEnrollment([Uniforme, TenueSport])).IsValid.Should().BeTrue();
    }

    [Fact]
    public void A_Selection_With_Duplicates_Should_Fail()
    {
        var result = _validator.Validate(ReEnrollment([Uniforme, Uniforme]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEnrollmentCommand.OptionalFeeCategoryIds));
    }

    [Fact]
    public void A_Selection_With_An_Empty_Guid_Should_Fail()
    {
        var result = _validator.Validate(ReEnrollment([Guid.Empty]));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateEnrollmentCommand.OptionalFeeCategoryIds));
    }
}
