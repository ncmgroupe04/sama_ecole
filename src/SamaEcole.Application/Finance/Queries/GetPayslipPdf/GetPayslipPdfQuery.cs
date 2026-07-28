using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetPayslipPdf;

public record GetPayslipPdfQuery(Guid FichePaieId) : IRequest<PayslipPdfResult>;

public record PayslipPdfResult(byte[] Content, string PayslipNumber);

public class GetPayslipPdfQueryHandler(
    ISender mediator,
    IPayslipPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetPayslipPdfQuery, PayslipPdfResult>
{
    public async Task<PayslipPdfResult> Handle(GetPayslipPdfQuery request, CancellationToken cancellationToken)
    {
        var payslip = await mediator.Send(new GetPayslipQuery(request.FichePaieId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(payslip.SchoolLogoUrl, cancellationToken);

        return new PayslipPdfResult(pdfGenerator.Generate(payslip, logo), payslip.PayslipNumber);
    }
}
