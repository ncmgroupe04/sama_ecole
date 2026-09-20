using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class SubjectSectionTypeTests
{
    [Fact]
    public void A_New_Subject_Defaults_To_French_Section()
    {
        var subject = new Subject
        {
            SchoolId = Guid.NewGuid(),
            Name = "Mathématiques",
            Level = "Primaire",
            Coefficient = 4
        };

        subject.SectionType.Should().Be(SectionType.French);
    }
}
