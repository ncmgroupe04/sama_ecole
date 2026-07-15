using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;
using MediatR;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceiptPdf;

/// <summary>
/// GET /api/v1/enrollments/{id}/receipt/pdf — ticket JGK-E02. Le reçu officiel en PDF, téléchargeable
/// depuis l'écran d'inscription. On NE recharge PAS les données ici : on réutilise
/// <see cref="GetEnrollmentReceiptQuery"/> (isolation tenant, lignes figées, 404 hors établissement),
/// puis on ne fait plus que composer le document. Une seule source de vérité pour le contenu du reçu,
/// que l'appelant veuille le JSON ou le PDF.
/// </summary>
public record GetEnrollmentReceiptPdfQuery(Guid EnrollmentId) : IRequest<ReceiptPdfResult>;

/// <summary>Le PDF rendu et le numéro officiel, ce dernier servant à nommer le fichier téléchargé.</summary>
public record ReceiptPdfResult(byte[] Content, string ReceiptNumber);

public class GetEnrollmentReceiptPdfQueryHandler(
    ISender mediator,
    IReceiptPdfGenerator pdfGenerator)
    : IRequestHandler<GetEnrollmentReceiptPdfQuery, ReceiptPdfResult>
{
    public async Task<ReceiptPdfResult> Handle(GetEnrollmentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(new GetEnrollmentReceiptQuery(request.EnrollmentId), cancellationToken);

        return new ReceiptPdfResult(pdfGenerator.Generate(receipt), receipt.ReceiptNumber);
    }
}
