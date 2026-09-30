using SamaEcole.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Schools;

public class SchoolSettingsProfileEtablissementTests
{
    [Fact]
    public void New_School_Settings_Default_To_No_Profile()
    {
        // NULL, jamais une valeur : c'est ce qui déclenche la redirection vers /onboarding
        // (voir ProfileEtablissement, SamaEcole.Domain.Enums.CommonEnums).
        var settings = new SchoolSettings { SchoolId = Guid.NewGuid() };

        settings.ProfileEtablissement.Should().BeNull();
    }
}
