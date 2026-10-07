using FluentAssertions;
using SamaEcole.Application.Boarding;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class BoardingOccupancyTests
{
    [Theory]
    [InlineData(BedStatus.Available, false, BedStatus.Available)]
    [InlineData(BedStatus.Available, true, BedStatus.Occupied)]
    [InlineData(BedStatus.Maintenance, false, BedStatus.Maintenance)]
    [InlineData(BedStatus.Maintenance, true, BedStatus.Maintenance)] // la maintenance prime, jamais deux états
    public void Status_Is_Projected_From_The_Stored_Status_And_The_Active_Stay(
        BedStatus stored, bool hasActiveStay, BedStatus expected)
        => BoardingOccupancy.StatusOf(stored, hasActiveStay).Should().Be(expected);

    [Theory]
    [InlineData(0, 0, 0, 0.0)]
    [InlineData(5, 10, 0, 0.5)]
    [InlineData(5, 10, 2, 0.625)]  // les lits en maintenance ne comptent pas dans la capacité utilisable
    [InlineData(0, 4, 4, 0.0)]     // tout en maintenance : jamais de division par zéro
    public void Rate_Excludes_Beds_In_Maintenance(int occupied, int capacity, int maintenance, double expected)
        => ((double)BoardingOccupancy.Rate(occupied, capacity, maintenance)).Should().BeApproximately(expected, 1e-9);
}
