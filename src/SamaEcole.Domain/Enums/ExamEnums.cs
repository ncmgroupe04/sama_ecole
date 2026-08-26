namespace SamaEcole.Domain.Enums;

/// <summary>
/// Type d'examen officiel couvert par le module (Volume 1 §22). Détermine si une série est
/// pertinente : CFEE n'en a pas, BFEM et BAC en ont une (<see cref="Entities.ExamSession.Series"/>).
/// </summary>
public enum ExamType
{
    CFEE,
    BFEM,
    BAC
}

/// <summary>Cycle de vie d'une session (campagne) d'examen.</summary>
public enum ExamSessionStatus
{
    EnPreparation,
    InscriptionsOuvertes,
    Transmis,
    Clos
}

/// <summary>
/// Cycle de vie d'un dossier de candidature. Un dossier <c>Incomplet</c> ne peut jamais devenir
/// <c>Transmis</c> — c'est l'audit (GetExamDossierAuditQuery) qui fait foi, pas une case cochée
/// manuellement (Volume 1 §22.3).
/// </summary>
public enum ExamDossierStatus
{
    Incomplet,
    Complet,
    Transmis,
    Valide
}

/// <summary>
/// Mention obtenue à la délibération. Sans objet pour le CFEE, qui n'attribue pas de mention —
/// <see cref="Entities.ExamResult.Mention"/> reste alors nul (Volume 1 §22.6).
/// </summary>
public enum ExamMention
{
    Passable,
    AssezBien,
    Bien,
    TresBien
}
