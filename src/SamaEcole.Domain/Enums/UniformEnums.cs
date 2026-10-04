namespace SamaEcole.Domain.Enums;

/// <summary>Nature d'un modèle de tenue (module Tenues). Sert au tri et au filtrage, jamais à un calcul.</summary>
public enum UniformKind
{
    Uniforme,
    TenueSport,
    Accessoire
}

/// <summary>
/// Public visé par un modèle de tenue. Sert à PROPOSER les modèles adaptés à un élève (comparé à
/// <c>Student.Gender</c>, « M » ou « F »), jamais à en interdire un : une école reste libre de remettre
/// n'importe quelle tenue à n'importe quel élève.
/// </summary>
public enum UniformGender
{
    Mixte,
    Garcon,
    Fille
}

/// <summary>
/// Cycle de vie d'une dotation (ligne d'un élève pour un modèle de tenue). Persisté en chaîne, comme tous les
/// enums métier.
///
/// <c>Requested → Delivered → Returned</c>, ou <c>Requested → Cancelled</c>. Une tenue remise ne s'annule jamais :
/// elle se RETOURNE, ce qui écrit un mouvement de stock inverse (le journal est append-only).
/// </summary>
public enum StudentUniformStatus
{
    Requested,
    Delivered,
    Returned,
    Cancelled
}

/// <summary>
/// Facturation d'une dotation, calculée à la remise puis FIGÉE sur la ligne (Finance reste en lecture seule,
/// AGENTS.md règle #4). <see cref="NotBilled"/> exige un motif : dotation gratuite, don, achat hors inscription…
/// </summary>
public enum UniformBillingStatus
{
    Billed,
    NotBilled
}
