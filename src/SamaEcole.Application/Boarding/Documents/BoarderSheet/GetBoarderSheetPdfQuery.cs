using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Documents.BoarderSheet;

/// <summary>
/// GET /api/v1/boarding/boarders/{id}/pdf — la fiche individuelle d'un pensionnaire en PDF. Une impression est une action
/// tracée (<see cref="IAuditableRequest"/>) : le journal ne retient que le module, l'action, l'auteur et le résultat, jamais
/// le contenu du document. La fiche médicale n'y figure que pour le Directeur et le Surveillant (voir
/// <see cref="GetBoarderSheetQuery"/>).
/// </summary>
public record GetBoarderSheetPdfQuery(Guid BoarderId) : IRequest<BoardingPdfResult>, IAuditableRequest;

public class GetBoarderSheetPdfQueryHandler(
    ISender mediator, IBoarderSheetPdfGenerator pdfGenerator, ISchoolLogoProvider logoProvider)
    : IRequestHandler<GetBoarderSheetPdfQuery, BoardingPdfResult>
{
    public async Task<BoardingPdfResult> Handle(GetBoarderSheetPdfQuery request, CancellationToken cancellationToken)
    {
        // 404 (séjour hors école) est levé ici ; le DTO arrive déjà masqué selon le rôle de l'appelant.
        var sheet = await mediator.Send(new GetBoarderSheetQuery(request.BoarderId), cancellationToken);

        var logo = await logoProvider.TryFetchAsync(sheet.SchoolLogoUrl, cancellationToken);

        return new BoardingPdfResult(pdfGenerator.Generate(sheet, logo), sheet.Boarder.Matricule);
    }
}
