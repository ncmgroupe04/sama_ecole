using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.UpdateMention;

/// <summary>
/// PATCH /grades/mentions/{id} — corrige le libellé ou le seuil d'une mention déjà créée, sans passer
/// par une suppression puis une recréation (qui aurait, elle, réinitialisé TOUTE la liste : voir le
/// commentaire de GetMentionsQueryHandler — dès la première mention stockée, l'école quitte
/// définitivement les valeurs par défaut).
/// </summary>
public record UpdateMentionCommand(Guid Id, string Label, decimal MinAverage) : IRequest<MentionDto>;

public class UpdateMentionCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpdateMentionCommand, MentionDto>
{
    public async Task<MentionDto> Handle(UpdateMentionCommand request, CancellationToken cancellationToken)
    {
        _ = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Même barème de RÉFÉRENCE que CreateMentionCommand (voir MentionScales) — jamais celui de
        // l'école, qui ne pilote plus aucune note.
        GradingScaleGuard.EnsureWithinScale(request.MinAverage, MentionScales.Reference, nameof(request.MinAverage));

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser la
        // mention d'une autre école renvoie 404, jamais une modification silencieuse.
        var mention = await dbContext.Mentions
            .FirstOrDefaultAsync(m => m.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Mention {request.Id} introuvable.");

        // Un libellé renommé vers un doublon viole l'index unique (SchoolId, Label, IsDeleted) :
        // SaveChangesAsync le traduit en ConcurrencyConflictException (409), jamais un écrasement
        // silencieux (même raisonnement que CreateMentionCommandHandler).
        mention.Label = request.Label.Trim();
        mention.MinAverage = request.MinAverage;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new MentionDto(mention.Id, mention.Label, mention.MinAverage);
    }
}
