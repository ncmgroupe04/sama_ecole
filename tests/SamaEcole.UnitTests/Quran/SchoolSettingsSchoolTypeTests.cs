using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class SchoolSettingsSchoolTypeTests
{
    [Fact]
    public void New_School_Settings_Default_To_Standard_School_Type()
    {
        var settings = new SchoolSettings { SchoolId = Guid.NewGuid() };

        settings.SchoolType.Should().Be(SchoolType.Standard);
    }
}
