using FluentAssertions;
using SamaEcole.Application.Boarding;
using SamaEcole.Application.Boarding.Boarders.ListBoarders;
using SamaEcole.Application.Boarding.Boarders.UpdateBoarderProfile;
using Xunit;

namespace SamaEcole.UnitTests.Boarding;

public class BoarderProfileValidatorTests
{
    private readonly UpdateBoarderProfileCommandValidator _profile = new();
    private readonly ListBoardersQueryValidator _list = new();

    private static UpdateBoarderProfileCommand Valid() => new(
        Guid.NewGuid(), "Allergie aux arachides", "Mme Diop", "77 123 45 67",
        [new AllowedExitPersonDto("Oumar Diop", "Oncle", "76 123 45 67")], 0);

    [Fact] public void A_Complete_Profile_Is_Valid() => _profile.Validate(Valid()).IsValid.Should().BeTrue();

    [Fact] public void Every_Field_Is_Optional() =>
        _profile.Validate(Valid() with { MedicalNotes = null, EmergencyContactName = null, EmergencyContactPhone = null, AllowedExitPersons = [] })
            .IsValid.Should().BeTrue();

    [Fact] public void Medical_Notes_Are_Limited_To_2000_Characters() =>
        _profile.Validate(Valid() with { MedicalNotes = new string('x', 2001) }).IsValid.Should().BeFalse();

    [Fact] public void Html_In_Medical_Notes_Is_Refused() =>
        _profile.Validate(Valid() with { MedicalNotes = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();

    [Fact] public void The_Emergency_Phone_Must_Be_A_Senegalese_Number() =>
        _profile.Validate(Valid() with { EmergencyContactPhone = "abc" }).IsValid.Should().BeFalse();

    [Fact] public void The_Emergency_Name_Is_Limited_To_150_Characters() =>
        _profile.Validate(Valid() with { EmergencyContactName = new string('x', 151) }).IsValid.Should().BeFalse();

    [Fact] public void At_Most_Ten_Authorized_Persons() =>
        _profile.Validate(Valid() with
        {
            AllowedExitPersons = Enumerable.Range(0, 11).Select(i => new AllowedExitPersonDto($"P{i}", "Oncle", "76 123 45 67")).ToList()
        }).IsValid.Should().BeFalse();

    [Fact] public void An_Authorized_Person_Needs_A_Name_And_A_Valid_Phone() =>
        _profile.Validate(Valid() with
        {
            AllowedExitPersons = [new AllowedExitPersonDto("", "Oncle", "76 123 45 67"), new AllowedExitPersonDto("X", "Oncle", "abc")]
        }).IsValid.Should().BeFalse();

    [Fact] public void A_Relationship_Is_Limited_To_50_Characters() =>
        _profile.Validate(Valid() with
        {
            AllowedExitPersons = [new AllowedExitPersonDto("X", new string('x', 51), "76 123 45 67")]
        }).IsValid.Should().BeFalse();

    [Theory]
    [InlineData("active", true)]
    [InlineData("ended", true)]
    [InlineData("awaitingBed", true)]
    [InlineData("ACTIVE", true)]
    [InlineData("onLeave", false)]   // reporté au lot D (sorties)
    [InlineData("nimporte", false)]
    public void The_List_Status_Is_One_Of_The_Known_Values(string status, bool expected) =>
        _list.Validate(new ListBoardersQuery { Status = status }).IsValid.Should().Be(expected);
}
