using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPaymentReceipt;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPaymentReceiptPdf;

/// <summary>
/// GET /finance/payments/{id}/receipt/pdf — ticket JGK-F02. Le reçu de paiement officiel en PDF. On NE
/// recharge PAS les données ici : on réutilise <see cref="GetPaymentReceiptQuery"/> (isolation tenant,
/// montants figés, 404 hors établissement), puis on compose. Le logo est récupéré ici (E/S réseau avec
/// garde SSRF) avant la composition — un logo absent/injoignable rend le reçu sans logo, jamais en erreur.
/// </summary>
public record GetPaymentReceiptPdfQuery(Guid PaymentId) : IRequest<PaymentReceiptPdfResult>;

/// <summary>Le PDF rendu et le numéro officiel, ce dernier servant à nommer le fichier téléchargé.</summary>
public record PaymentReceiptPdfResult(byte[] Content, string ReceiptNumber);

public class GetPaymentReceiptPdfQueryHandler(
    ISender mediator,
    IPaymentReceiptPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetPaymentReceiptPdfQuery, PaymentReceiptPdfResult>
{
    public async Task<PaymentReceiptPdfResult> Handle(GetPaymentReceiptPdfQuery request, CancellationToken cancellationToken)
    {
        var receipt = await mediator.Send(new GetPaymentReceiptQuery(request.PaymentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(receipt.SchoolLogoUrl, cancellationToken);

        return new PaymentReceiptPdfResult(pdfGenerator.Generate(receipt, logo), receipt.ReceiptNumber);
    }
}
