using FluentAssertions;
using SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;
using Xunit;

namespace SamaEcole.UnitTests.ClassJournal;

public class CreateClassJournalEntryCommandValidatorTests
{
    private readonly CreateClassJournalEntryCommandValidator _validator = new();

    private static CreateClassJournalEntryCommand Valid() => new()
    {
        ClassroomId = Guid.NewGuid(),
        SubjectId = Guid.NewGuid(),
        SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
        Topic = "Le théorème de Pythagore",
        Content = "Démonstration au tableau, exercices 1 à 4."
    };

    [Fact]
    public void Valid_Entry_Should_Pass()
    {
        _validator.Validate(Valid()).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Future_Session_Date_Should_Fail()
    {
        // On journalise ce qui a été fait, jamais un programme prévisionnel (ticket JGK-P04).
        var command = Valid() with { SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Todays_Session_Date_Should_Pass()
    {
        var command = Valid() with { SessionDate = DateOnly.FromDateTime(DateTime.UtcNow) };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Topic_Should_Fail()
    {
        _validator.Validate(Valid() with { Topic = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Empty_Content_Should_Fail()
    {
        _validator.Validate(Valid() with { Content = "" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Topic_With_Html_Should_Fail()
    {
        // JGK-F01 — branchement de la règle NoHtml (payloads exhaustifs : SafeTextValidationTests).
        _validator.Validate(Valid() with { Topic = "<script>alert(1)</script>" }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Homework_Due_Date_Without_Homework_Should_Fail()
    {
        // Une date de rendu n'a de sens que si un devoir est renseigné.
        var command = Valid() with { Homework = null, HomeworkDueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3)) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Homework_Due_Date_Before_Session_Date_Should_Fail()
    {
        var sessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var command = Valid() with { SessionDate = sessionDate, Homework = "Exercices 5 à 8", HomeworkDueDate = sessionDate.AddDays(-1) };

        _validator.Validate(command).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Homework_With_Due_Date_On_Or_After_Session_Should_Pass()
    {
        var sessionDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var command = Valid() with { SessionDate = sessionDate, Homework = "Exercices 5 à 8", HomeworkDueDate = sessionDate.AddDays(3) };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Homework_Without_Due_Date_Should_Pass()
    {
        // La date de rendu reste facultative même quand un devoir est donné.
        var command = Valid() with { Homework = "Réviser la leçon", HomeworkDueDate = null };

        _validator.Validate(command).IsValid.Should().BeTrue();
    }
}
