using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Documents.DormitoryRegister;

/// <summary>
/// GET /api/v1/boarding/dormitories/{id}/register-pdf?date= — le registre imprimable d'un pavillon pour une nuit (aujourd'hui
/// par défaut). Action tracée comme tout document imprimé. Aucune donnée de santé ni téléphone dans ce document.
/// </summary>
public record GetDormitoryRegisterPdfQuery(Guid DormitoryId, DateOnly? Date) : IRequest<BoardingPdfResult>, IAuditableRequest;

public class GetDormitoryRegisterPdfQueryHandler(
    ISender mediator, IDormitoryRegisterPdfGenerator pdfGenerator, ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetDormitoryRegisterPdfQuery, BoardingPdfResult>
{
    public async Task<BoardingPdfResult> Handle(GetDormitoryRegisterPdfQuery request, CancellationToken cancellationToken)
    {
        // 404 (pavillon hors école) et 422 (date future ou hors année active) sont levés ici.
        var register = await mediator.Send(new GetDormitoryRegisterQuery(request.DormitoryId, request.Date), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(register.SchoolLogoUrl, cancellationToken);

        return new BoardingPdfResult(
            pdfGenerator.Generate(register, logo), $"{register.DormitoryName}-{register.Date:yyyy-MM-dd}");
    }
}
