using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Exams.Common;

namespace SamaEcole.Application.Exams.Queries.GetExamSynthesisPdf;

/// <summary>
/// GET /api/v1/exams/sessions/{id}/pv/pdf — procès-verbal de la session : un tableau, un candidat par ligne
/// (numéro de table, IEN, nom en français et en arabe, moyennes par section, résultat). Ne retient que les dossiers
/// Complet, Transmis ou Valide — 422 s'il n'y en a aucun.
/// </summary>
public record GetExamSessionPvPdfQuery(Guid ExamSessionId) : IRequest<byte[]>;

/// <summary>GET /api/v1/exams/dossiers/{id}/synthesis/pdf — fiche synthétique d'un candidat, quel que soit le statut de son dossier.</summary>
public record GetExamCandidateSynthesisPdfQuery(Guid DossierId) : IRequest<byte[]>;

public class GetExamSynthesisPdfQueryHandlers(
    IApplicationDbContext dbContext,
    ExamSynthesisBuilder builder,
    ISchoolLogoProvider logoProvider,
    IExamSynthesisPdfGenerator pdfGenerator)
    : IRequestHandler<GetExamSessionPvPdfQuery, byte[]>,
      IRequestHandler<GetExamCandidateSynthesisPdfQuery, byte[]>
{
    public async Task<byte[]> Handle(GetExamSessionPvPdfQuery request, CancellationToken cancellationToken)
    {
        var model = await builder.BuildAsync(request.ExamSessionId, onlyDossierId: null, cancellationToken);
        return pdfGenerator.GenerateSessionPv(model, await LogoAsync(cancellationToken));
    }

    public async Task<byte[]> Handle(GetExamCandidateSynthesisPdfQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : un dossier d'une autre école est introuvable.
        var sessionId = await dbContext.ExamDossiers.AsNoTracking()
            .Where(d => d.Id == request.DossierId)
            .Select(d => (Guid?)d.ExamSessionId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Dossier d'examen {request.DossierId} introuvable.");

        var model = await builder.BuildAsync(sessionId, request.DossierId, cancellationToken);
        return pdfGenerator.GenerateCandidateSheet(model, await LogoAsync(cancellationToken));
    }

    private async Task<byte[]?> LogoAsync(CancellationToken cancellationToken)
    {
        var logoUrl = await dbContext.Schools.AsNoTracking().Select(s => s.LogoUrl).FirstOrDefaultAsync(cancellationToken);
        return await logoProvider.TryFetchAsync(logoUrl, cancellationToken);
    }
}
