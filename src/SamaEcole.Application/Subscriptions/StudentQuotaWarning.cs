namespace SamaEcole.Application.Subscriptions;

/// <summary>
/// Renvoyé AVEC une création réussie quand l'effectif dépasse désormais le plafond nominal, sans avoir
/// atteint la tolérance : l'élève est bien créé, l'écran affiche un bandeau d'avertissement. Absent
/// (<c>null</c>) tant que l'école reste dans son plafond nominal.
/// </summary>
/// <param name="CurrentStudentCount">Effectif APRÈS la création.</param>
/// <param name="RemainingBeforeBlock">Élèves encore admis avant le blocage strict (Soft cap − effectif).</param>
public sealed record StudentQuotaWarning(
    int CurrentStudentCount,
    int MaxStudentLimit,
    int SoftQuotaLimit,
    int RemainingBeforeBlock);
