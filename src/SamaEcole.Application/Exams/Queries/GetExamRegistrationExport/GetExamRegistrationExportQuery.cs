using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamRegistrationExport;

/// <summary>
/// GET /api/v1/exams/export/ministerial — export Excel du relevé d'inscription d'une session, au
/// format attendu par l'IEF/l'IA. Colonnes provisoires (Volume 1 §22.5) : à faire valider contre un
/// gabarit ministériel réel avant mise en production — voir ticket JGK-J06.
/// </summary>
public record GetExamRegistrationExportQuery(Guid ExamSessionId) : IRequest<ExamRegistrationExportFile>;

public record ExamRegistrationExportFile(byte[] Content, string FileName);

public record ExamRegistrationRow(
    string? CandidateNumber,
    string StudentFullName,
    string StudentMatricule,
    DateOnly BirthDate,
    string BirthPlace,
    string Gender,
    string ClassroomName,
    string? Series,
    string? ExamCenterName,
    string Status);
