using SamaEcole.Application.Exams.Commands.RecordExamResult;
using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamDossierDetail;

/// <summary>GET /api/v1/exams/dossiers/{id} — fiche complète d'un dossier.</summary>
public record GetExamDossierDetailQuery(Guid Id) : IRequest<ExamDossierDetail>;

/// <summary>
/// Champs à plat (pas d'objet imbriqué) : le JSON produit correspond exactement à la composition
/// <c>allOf</c> d'ExamDossierListItem dans openapi.yaml — ce n'est qu'une façon différente d'écrire
/// le même contrat côté C#.
/// </summary>
public record ExamDossierDetail(
    Guid Id,
    Guid ExamSessionId,
    Guid StudentId,
    string StudentFullName,
    Guid ClassroomId,
    string ClassroomName,
    string? CandidateNumber,
    string? ExamCenterName,
    bool BirthCertificatePresent,
    bool? CivilStatusConforming,
    string Status,
    uint RowVersion,
    string? BirthCertificateNumber,
    string? CivilStatusNotes,
    DateOnly? TransmittedOn,
    ExamResultDto? Result,
    // Carte scolaire et état civil (Volume 1 §23.4) : code officiel du centre (distinct du nom),
    // numéro de table (distinct du numéro de candidat), état détaillé de la pièce d'état civil.
    string? ExamCenterCode,
    string? TableNumber,
    string CivilRegistryDocumentStatus);
