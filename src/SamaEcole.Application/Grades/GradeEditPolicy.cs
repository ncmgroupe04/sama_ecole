using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Qui peut CORRIGER une note déjà saisie (Évolution N°1). Règle pure, partagée par la correction
/// unitaire (UpdateGradeCommandHandler), l'import Excel (qui modifie aussi des notes existantes) et
/// la grille de saisie (champ <c>CanEdit</c> de chaque cellule) — une seule définition, pour que
/// l'écran, l'API et l'import ne divergent jamais.
///
/// Directeur et Secrétariat corrigent toujours, sans limite de délai. L'Enseignant, lui, doit
/// satisfaire DEUX conditions : (1) la note a été saisie il y a au plus <c>windowDays</c> jours
/// (SchoolSettings.GradeEditWindowDays) ; (2) il en est l'AUTEUR, ou il est AFFECTÉ à la classe et à
/// la matière concernées — il ne peut pas retoucher la note posée par un collègue dans une autre
/// matière.
/// </summary>
public static class GradeEditPolicy
{
    public static bool CanCorrect(
        Role? role, DateTimeOffset createdAt, DateTimeOffset now, int windowDays, bool isAuthor, bool isAssigned)
        => role switch
        {
            Role.Directeur or Role.Secretariat or Role.SuperAdmin => true,
            Role.Enseignant => IsWithinWindow(createdAt, now, windowDays) && (isAuthor || isAssigned),
            _ => false
        };

    /// <summary>Borne INCLUSIVE : « le délai écoulé ne dépasse pas N jours » — exactement N jours passe encore.</summary>
    public static bool IsWithinWindow(DateTimeOffset createdAt, DateTimeOffset now, int windowDays)
        => now - createdAt <= TimeSpan.FromDays(windowDays);
}
