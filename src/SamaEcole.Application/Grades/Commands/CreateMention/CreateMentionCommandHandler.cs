using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades.Queries.GetMentions;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.CreateMention;

public class CreateMentionCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateMentionCommand, MentionDto>
{
    public async Task<MentionDto> Handle(CreateMentionCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Un seuil de mention s'exprime sur le barème de RÉFÉRENCE (/20), pas sur celui de l'école :
        // la mention n'existe qu'au secondaire, et une école restée à « 10 » plafonnait ici à 10 des
        // seuils qui doivent pouvoir aller jusqu'à 20 (voir MentionScales).
        GradingScaleGuard.EnsureWithinScale(request.MinAverage, MentionScales.Reference, nameof(request.MinAverage));

        // Un libellé en doublon viole l'index unique : SaveChangesAsync le traduit en
        // ConcurrencyConflictException (409), jamais en écrasement silencieux (AGENTS.md règle #5).
        var mention = new Mention { SchoolId = schoolId, Label = request.Label.Trim(), MinAverage = request.MinAverage };

        dbContext.Mentions.Add(mention);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new MentionDto(mention.Id, mention.Label, mention.MinAverage);
    }
}
