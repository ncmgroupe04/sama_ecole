using FluentAssertions;
using SamaEcole.Application.Boarding.Documents.DormitoryRegister;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

/// <summary>
/// Verrou de confidentialité du registre du pavillon (spec §5.2) : il est imprimé et posé dans le pavillon, il ne doit
/// JAMAIS porter de donnée de santé ni de numéro de téléphone. Si quelqu'un ajoute un tel champ au DTO, ce test échoue.
/// </summary>
public class DormitoryRegisterDtoShapeTests
{
    private static readonly string[] Forbidden = ["medical", "phone", "contact", "allowedexit", "emergency", "health", "sante"];

    [Theory]
    [InlineData(typeof(DormitoryRegisterDto))]
    [InlineData(typeof(DormitoryRegisterRowDto))]
    public void No_Member_Of_The_Register_Can_Carry_Health_Or_Phone_Data(Type dto)
    {
        var names = dto.GetProperties().Select(p => p.Name);

        names.Should().NotContain(name => Forbidden.Any(f => name.Contains(f, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void The_Register_Keeps_The_Supervisors_Name_But_Not_The_Phone() =>
        typeof(DormitoryRegisterDto).GetProperties().Select(p => p.Name)
            .Should().Contain("SupervisorName").And.NotContain("SupervisorPhone");
}
