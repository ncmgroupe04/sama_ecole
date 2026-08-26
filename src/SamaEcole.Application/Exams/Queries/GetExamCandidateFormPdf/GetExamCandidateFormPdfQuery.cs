using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamCandidateFormPdf;

/// <summary>GET /api/v1/exams/dossiers/{id}/candidate-form/pdf — fiche de candidature individuelle.</summary>
public record GetExamCandidateFormPdfQuery(Guid DossierId) : IRequest<byte[]>;
