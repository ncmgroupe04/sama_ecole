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
///
/// Le logo de l'établissement est récupéré ici, avant la composition : c'est une E/S réseau (garde
/// SSRF, timeout, taille), elle n'a donc pas sa place dans le générateur, qui reste une fonction pure.
/// Un logo absent ou injoignable rend simplement <c>null</c> — le reçu s'émet sans logo, jamais en erreur.
///
/// IAuditableRequest (JGK-H01) : « impressions »/« exports » — c'est la seule action liée au reçu que
/// le SERVEUR observe réellement (le bouton Imprimer natif du navigateur ne fait aucun aller-retour serveur).
/// </summary>
public record GetEnrollmentReceiptPdfQuery(Guid EnrollmentId) : IRequest<ReceiptPdfResult>, IAuditableRequest;

/// <summary>Le PDF rendu et le numéro officiel, ce dernier servant à nommer le fichier téléchargé.</summary>
public record ReceiptPdfResult(byte[] Content, string ReceiptNumber);

public class GetEnrollmentReceiptPdfQueryHandler(
    ISender mediator,
    IReceiptPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetEnrollmentReceiptPdfQuery, ReceiptPdfResult>
{
    public async Task<ReceiptPdfResult> Handle(GetEnrollmentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(new GetEnrollmentReceiptQuery(request.EnrollmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(receipt.SchoolLogoUrl, cancellationToken);

        return new ReceiptPdfResult(pdfGenerator.Generate(receipt, logo), receipt.ReceiptNumber);
    }
}
