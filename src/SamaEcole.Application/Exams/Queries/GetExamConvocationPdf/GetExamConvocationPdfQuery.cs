using MediatR;

namespace SamaEcole.Application.Exams.Queries.GetExamConvocationPdf;

/// <summary>
/// GET /api/v1/exams/dossiers/{id}/convocation/pdf — carte de convocation individuelle. Refusée
/// (409) tant que centre et numéro de table ne sont pas attribués (Volume 1 §22.5).
/// </summary>
public record GetExamConvocationPdfQuery(Guid DossierId) : IRequest<byte[]>;
