using FluentAssertions;
using FluentValidation.TestHelper;
using SamaEcole.Application.Uniforms;
using SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;
using SamaEcole.Application.Uniforms.Commands.CreateUniformItem;
using SamaEcole.Application.Uniforms.Commands.CreateUniformVariant;
using SamaEcole.Application.Uniforms.Commands.UpdateStudentUniform;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Uniforms;

/// <summary>
/// Module Tenues, Phases 2 et 3 — la FORME des requêtes d'écriture, avant que les Handlers ne touchent à la base.
/// Les bornes reprennent celles des colonnes (nom 100, taille 30, notes 300) : une valeur trop longue doit être
/// refusée en 422 lisible, pas en erreur de base.
/// </summary>
public class UniformValidatorsTests
{
    private static readonly Guid Item = Guid.Parse("00000000-0000-0000-0000-0000000000a1");
    private static readonly Guid Other = Guid.Parse("00000000-0000-0000-0000-0000000000a2");

    // ------------------------------------------------------------------ modèle

    [Fact]
    public void A_Valid_Item_Passes()
    {
        var command = new CreateUniformItemCommand { Name = "Polo bleu", QuantityIncluded = 2, Notes = "Col rond" };

        new CreateUniformItemCommandValidator().TestValidate(command).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Item_Needs_A_Name(string name)
    {
        new CreateUniformItemCommandValidator().TestValidate(new CreateUniformItemCommand { Name = name })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Fact]
    public void An_Item_Name_Is_Bounded_To_The_Column_Width()
    {
        var validator = new CreateUniformItemCommandValidator();

        validator.TestValidate(new CreateUniformItemCommand { Name = new string('a', UniformCatalogRules.MaxItemNameLength) })
            .ShouldNotHaveValidationErrorFor(x => x.Name);
        validator.TestValidate(new CreateUniformItemCommand { Name = new string('a', UniformCatalogRules.MaxItemNameLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.Name);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_Fee_Covers_At_Least_One_Unit(int quantityIncluded)
    {
        new CreateUniformItemCommandValidator()
            .TestValidate(new CreateUniformItemCommand { Name = "Polo", QuantityIncluded = quantityIncluded })
            .ShouldHaveValidationErrorFor(x => x.QuantityIncluded);
    }

    [Fact]
    public void Markup_Is_Refused_In_Free_Text()
    {
        var validator = new CreateUniformItemCommandValidator();

        validator.TestValidate(new CreateUniformItemCommand { Name = "<b>Polo</b>" }).ShouldHaveValidationErrorFor(x => x.Name);
        validator.TestValidate(new CreateUniformItemCommand { Name = "Polo", Notes = "<script>x</script>" })
            .ShouldHaveValidationErrorFor(x => x.Notes);
    }

    [Fact]
    public void An_Unknown_Enum_Value_Is_Refused()
    {
        new CreateUniformItemCommandValidator()
            .TestValidate(new CreateUniformItemCommand { Name = "Polo", Kind = (UniformKind)99 })
            .ShouldHaveValidationErrorFor(x => x.Kind);
    }

    // ------------------------------------------------------------------ taille

    [Fact]
    public void A_Size_Needs_A_Label_Within_The_Column_Width()
    {
        var validator = new CreateUniformVariantCommandValidator();

        validator.TestValidate(new CreateUniformVariantCommand { UniformItemId = Item, SizeLabel = "10 ans" })
            .ShouldNotHaveAnyValidationErrors();
        validator.TestValidate(new CreateUniformVariantCommand { UniformItemId = Item, SizeLabel = " " })
            .ShouldHaveValidationErrorFor(x => x.SizeLabel);
        validator.TestValidate(new CreateUniformVariantCommand
            { UniformItemId = Item, SizeLabel = new string('a', UniformCatalogRules.MaxSizeLabelLength + 1) })
            .ShouldHaveValidationErrorFor(x => x.SizeLabel);
    }

    // ------------------------------------------------------------------ demande de tenues

    [Fact]
    public void A_Request_Needs_At_Least_One_Line()
    {
        new CreateStudentUniformsCommandValidator()
            .TestValidate(new CreateStudentUniformsCommand { EnrollmentId = Item, Lines = [] })
            .ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void The_Same_Item_Cannot_Appear_Twice_In_One_Request()
    {
        // La quantité porte le nombre d'exemplaires ; deux lignes du même modèle brouilleraient l'unicité « une ligne active ».
        var command = new CreateStudentUniformsCommand
        {
            EnrollmentId = Item,
            Lines = [new StudentUniformRequestLine(Item), new StudentUniformRequestLine(Item, Quantity: 2)]
        };

        new CreateStudentUniformsCommandValidator().TestValidate(command).ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(CreateStudentUniformsCommandValidator.MaxQuantity, true)]
    [InlineData(CreateStudentUniformsCommandValidator.MaxQuantity + 1, false)]
    public void A_Line_Quantity_Stays_Within_Bounds(int quantity, bool valid)
    {
        var command = new CreateStudentUniformsCommand
        {
            EnrollmentId = Item, Lines = [new StudentUniformRequestLine(Item, Quantity: quantity)]
        };

        var result = new CreateStudentUniformsCommandValidator().TestValidate(command);

        if (valid) result.ShouldNotHaveAnyValidationErrors();
        else result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void A_Request_Is_Bounded_In_Size()
    {
        var lines = Enumerable.Range(0, CreateStudentUniformsCommandValidator.MaxLines + 1)
            .Select(_ => new StudentUniformRequestLine(Guid.NewGuid()))
            .ToList();

        new CreateStudentUniformsCommandValidator()
            .TestValidate(new CreateStudentUniformsCommand { EnrollmentId = Item, Lines = lines })
            .ShouldHaveValidationErrorFor(x => x.Lines);
    }

    [Fact]
    public void An_Empty_Guid_Is_Not_A_Size()
    {
        new UpdateStudentUniformCommandValidator()
            .TestValidate(new UpdateStudentUniformCommand { Id = Item, UniformVariantId = Guid.Empty, Quantity = 1 })
            .ShouldHaveValidationErrorFor(x => x.UniformVariantId);
    }

    [Fact]
    public void An_Update_May_Clear_The_Size()
    {
        new UpdateStudentUniformCommandValidator()
            .TestValidate(new UpdateStudentUniformCommand { Id = Item, UniformVariantId = null, Quantity = 2 })
            .ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void An_Update_Quantity_Stays_Within_Bounds()
    {
        var validator = new UpdateStudentUniformCommandValidator();

        validator.TestValidate(new UpdateStudentUniformCommand { Id = Item, Quantity = 0 }).ShouldHaveValidationErrorFor(x => x.Quantity);
        validator.TestValidate(new UpdateStudentUniformCommand { Id = Other, Quantity = 3 }).ShouldNotHaveAnyValidationErrors();
    }
}
