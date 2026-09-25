using FluentAssertions;
using SamaEcole.Application.Attendance;
using Xunit;

namespace SamaEcole.UnitTests.Attendance;

public class DurationTextTests
{
    [Theory]
    [InlineData(0, "0 min")]
    [InlineData(45, "45 min")]
    [InlineData(60, "1 h")]
    [InlineData(120, "2 h")]
    [InlineData(140, "2 h 20")]
    [InlineData(185, "3 h 05")]
    public void Durations_Read_In_Plain_French(int minutes, string expected)
        => DurationText.Human(minutes).Should().Be(expected);
}
