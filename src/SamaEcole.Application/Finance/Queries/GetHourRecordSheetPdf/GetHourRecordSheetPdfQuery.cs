using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetHourRecordSheet;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetHourRecordSheetPdf;

public record GetHourRecordSheetPdfQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<HourRecordSheetPdfResult>, IAuditableRequest;

public record HourRecordSheetPdfResult(byte[] Content, string SheetNumber);

public class GetHourRecordSheetPdfQueryHandler(
    ISender mediator,
    IHourRecordSheetPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetHourRecordSheetPdfQuery, HourRecordSheetPdfResult>
{
    public async Task<HourRecordSheetPdfResult> Handle(GetHourRecordSheetPdfQuery request, CancellationToken cancellationToken)
    {
        var sheet = await mediator.Send(new GetHourRecordSheetQuery(request.EmployeeContractId, request.Month, request.Year), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(sheet.SchoolLogoUrl, cancellationToken);

        return new HourRecordSheetPdfResult(pdfGenerator.Generate(sheet, logo), sheet.SheetNumber);
    }
}
