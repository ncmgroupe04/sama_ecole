using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.AssignExamCenter;

/// <summary>
/// POST /api/v1/exams/dossiers/{id}/assign-center — attribue centre d'examen et numéro de table.
/// Le numéro est généré DANS la transaction de cet endpoint, jamais à l'ouverture du dossier
/// (AGENTS.md règle #3, même contrat que le matricule).
/// </summary>
public record AssignExamCenterCommand : IRequest<ExamDossierResult>
{
    public Guid Id { get; init; }

    /// <summary>Vide = hérite du centre par défaut de la session.</summary>
    public string? ExamCenterName { get; init; }

    /// <summary>
    /// CODE officiel du centre communiqué par l'IA (Volume 1 §23.4), distinct du nom. Vide = inchangé
    /// (souvent inconnu à la première affectation, communiqué plus tard) : jamais effacé par un envoi
    /// à blanc, même contrat que <see cref="ExamCenterName"/>.
    /// </summary>
    public string? ExamCenterCode { get; init; }

    /// <summary>
    /// Numéro de TABLE (place en salle), distinct du numéro de candidat. Communiqué par le centre
    /// avant les épreuves et susceptible de changer d'une épreuve à l'autre. Vide = inchangé.
    /// </summary>
    public string? TableNumber { get; init; }

    /// <summary>Vide = génération automatique séquentielle. Renseigné = reprise d'une numérotation déjà communiquée par l'IEF/IA.</summary>
    public string? CandidateNumber { get; init; }

    public required uint RowVersion { get; init; }
}
