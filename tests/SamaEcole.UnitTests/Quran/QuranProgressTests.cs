using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace SamaEcole.UnitTests.Quran;

public class QuranProgressTests
{
    [Fact]
    public void A_New_Progress_Entry_Defaults_To_InProcess()
    {
        var entry = new QuranProgress
        {
            SchoolId = Guid.NewGuid(),
            StudentId = Guid.NewGuid(),
            JuzNumber = 1,
            HizbNumber = 1,
            SurahNumber = 1
        };

        entry.Status.Should().Be(QuranMemorizationStatus.InProcess);
        entry.IsDeleted.Should().BeFalse();
    }
}
