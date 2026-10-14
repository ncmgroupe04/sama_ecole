using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.SchoolCalendar.Commands.DeleteSchoolClosure;

/// <summary>DELETE /api/v1/school-calendar/closures/{id} — suppression LOGIQUE (règle #6).</summary>
public record DeleteSchoolClosureCommand(Guid Id) : IRequest;

public class DeleteSchoolClosureCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    ILogger<DeleteSchoolClosureCommandHandler> logger)
    : IRequestHandler<DeleteSchoolClosureCommand>
{
    public async Task Handle(DeleteSchoolClosureCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur non identifié.");

        var closure = await dbContext.SchoolClosures.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Fermeture {request.Id} introuvable.");

        closure.SoftDelete(actorId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Fermeture {ClosureId} supprimée (logiquement) par {UserId}.", closure.Id, actorId);
    }
}
