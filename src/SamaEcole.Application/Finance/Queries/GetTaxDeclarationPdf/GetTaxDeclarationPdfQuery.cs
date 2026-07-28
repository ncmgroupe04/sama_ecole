using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetTaxDeclaration;
using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetTaxDeclarationPdf;

public record GetTaxDeclarationPdfQuery(Guid TaxDeclarationId) : IRequest<TaxDeclarationPdfResult>;

public record TaxDeclarationPdfResult(byte[] Content, string DeclarationNumber);

public class GetTaxDeclarationPdfQueryHandler(
    ISender mediator,
    ITaxDeclarationPdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetTaxDeclarationPdfQuery, TaxDeclarationPdfResult>
{
    public async Task<TaxDeclarationPdfResult> Handle(GetTaxDeclarationPdfQuery request, CancellationToken cancellationToken)
    {
        var declaration = await mediator.Send(new GetTaxDeclarationQuery(request.TaxDeclarationId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(declaration.SchoolLogoUrl, cancellationToken);

        return new TaxDeclarationPdfResult(pdfGenerator.Generate(declaration, logo), declaration.DeclarationNumber);
    }
}
