using SamaEcole.Application.Exams.Commands.CreateExamDossier;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Exams.Commands.UpdateExamDossier;

/// <summary>
/// PUT /api/v1/exams/dossiers/{id} — corrige l'état civil ou le centre déclaré. Ne porte
/// délibérément aucun <c>ClassroomId</c> : ce champ est figé à l'ouverture (Volume 1 §22.1).
/// </summary>
public record UpdateExamDossierCommand : IRequest<ExamDossierResult>
{
    public Guid Id { get; init; }
    public string? ExamCenterName { get; init; }
    public string? BirthCertificateNumber { get; init; }
    public bool BirthCertificatePresent { get; init; }

    /// <summary>Nul = non encore contrôlé, distinct de <c>false</c> (Volume 1 §22.2).</summary>
    public bool? CivilStatusConforming { get; init; }

    /// <summary>
    /// État détaillé de la pièce d'état civil (Volume 1 §23.4). <c>null</c> = ne pas toucher à la
    /// valeur en base — un client qui n'envoie pas le champ ne doit pas le remettre à
    /// <see cref="CivilRegistryDocumentStatus.NonFourni"/> en silence. C'est aussi le SEUL point
    /// d'écriture de l'état <see cref="CivilRegistryDocumentStatus.EnRegularisation"/>
    /// (« fourni, non conforme, jugement supplétif en cours »), auparavant inatteignable.
    /// </summary>
    public CivilRegistryDocumentStatus? CivilRegistryDocumentStatus { get; init; }

    public string? CivilStatusNotes { get; init; }
    public required uint RowVersion { get; init; }
}
