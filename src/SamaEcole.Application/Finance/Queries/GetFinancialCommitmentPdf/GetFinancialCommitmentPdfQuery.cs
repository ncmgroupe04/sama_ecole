using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetFinancialCommitment;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetFinancialCommitmentPdf;

public record GetFinancialCommitmentPdfQuery(Guid FinancialCommitmentId) : IRequest<FinancialCommitmentPdfResult>, IAuditableRequest;

public record FinancialCommitmentPdfResult(byte[] Content, string CommitmentNumber);

public class GetFinancialCommitmentPdfQueryHandler(
    ISender mediator,
    IFinancialCommitmentPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetFinancialCommitmentPdfQuery, FinancialCommitmentPdfResult>
{
    public async Task<FinancialCommitmentPdfResult> Handle(GetFinancialCommitmentPdfQuery request, CancellationToken cancellationToken)
    {
        var commitment = await mediator.Send(new GetFinancialCommitmentQuery(request.FinancialCommitmentId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(commitment.SchoolLogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={commitment.CommitmentNumber}");

        return new FinancialCommitmentPdfResult(pdfGenerator.Generate(commitment, logo, qrCode), commitment.CommitmentNumber);
    }
}
