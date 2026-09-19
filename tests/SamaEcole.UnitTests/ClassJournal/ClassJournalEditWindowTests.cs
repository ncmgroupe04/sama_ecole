using FluentAssertions;
using SamaEcole.Application.ClassJournal;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Entities;
using Xunit;

namespace SamaEcole.UnitTests.ClassJournal;

/// <summary>Règle des 15 jours (ticket JGK-P04) — logique pure, aucune base nécessaire.</summary>
public class ClassJournalEditWindowTests
{
    private static readonly Guid Auteur = Guid.NewGuid();
    private static readonly Guid AutreEnseignant = Guid.NewGuid();
    private static readonly DateOnly Seance = new(2026, 9, 1);

    private static ClassJournalEntry Entry() => new()
    {
        SchoolId = Guid.NewGuid(), ClassroomId = Guid.NewGuid(), SubjectId = Guid.NewGuid(),
        TeacherId = Auteur, SessionDate = Seance, Topic = "Sujet", Content = "Contenu"
    };

    [Fact]
    public void Auteur_Le_Jour_Meme_Peut_Corriger()
    {
        ClassJournalEditWindow.CanCorrect(Entry(), Auteur, Seance).Should().BeTrue();
    }

    [Fact]
    public void Auteur_A_J_Plus_15_Peut_Encore_Corriger()
    {
        ClassJournalEditWindow.CanCorrect(Entry(), Auteur, Seance.AddDays(15)).Should().BeTrue();
    }

    [Fact]
    public void Auteur_A_J_Plus_16_Ne_Peut_Plus_Corriger()
    {
        ClassJournalEditWindow.CanCorrect(Entry(), Auteur, Seance.AddDays(16)).Should().BeFalse();
    }

    [Fact]
    public void Autre_Enseignant_Ne_Peut_Jamais_Corriger_Meme_Dans_Le_Delai()
    {
        ClassJournalEditWindow.CanCorrect(Entry(), AutreEnseignant, Seance).Should().BeFalse();
    }

    [Fact]
    public void Role_Non_Borne_Directeur_Secretariat_Peut_Toujours_Corriger()
    {
        // ownTeacherId null = rôle non borné (ClassJournalScopeAuthorizer.GetOwnTeacherIdOrNullAsync).
        ClassJournalEditWindow.CanCorrect(Entry(), ownTeacherId: null, Seance.AddDays(100)).Should().BeTrue();
    }

    [Fact]
    public void EnsureCanCorrect_Leve_Apres_Le_Delai_Pour_Lauteur()
    {
        var act = () => ClassJournalEditWindow.EnsureCanCorrect(Entry(), Auteur, Seance.AddDays(16));

        act.Should().Throw<ForbiddenException>().WithMessage("*15 jours*");
    }

    [Fact]
    public void EnsureCanCorrect_Leve_Pour_Un_Autre_Enseignant_Meme_Dans_Le_Delai()
    {
        var act = () => ClassJournalEditWindow.EnsureCanCorrect(Entry(), AutreEnseignant, Seance);

        act.Should().Throw<ForbiddenException>().WithMessage("*propres entrées*");
    }

    [Fact]
    public void EnsureCanCorrect_Ne_Leve_Pas_Pour_Le_Directeur()
    {
        var act = () => ClassJournalEditWindow.EnsureCanCorrect(Entry(), ownTeacherId: null, Seance.AddDays(100));

        act.Should().NotThrow();
    }
}
