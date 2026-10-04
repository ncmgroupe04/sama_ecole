using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.DeleteUniformVariant;

public class DeleteUniformVariantCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteUniformVariantCommand, Unit>
{
    public async Task<Unit> Handle(DeleteUniformVariantCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var variant = await dbContext.UniformVariants.FirstOrDefaultAsync(v => v.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Taille {request.Id} introuvable.");

        var hasPendingNeed = await dbContext.StudentUniforms.AnyAsync(
            u => u.UniformVariantId == variant.Id && u.Status == StudentUniformStatus.Requested, cancellationToken);

        if (hasPendingNeed)
        {
            throw new BusinessRuleException(
                "Impossible d'archiver cette taille : des élèves l'attendent encore. Changez leur taille ou annulez ces besoins d'abord.",
                "UNIFORM_VARIANT_IN_USE");
        }

        variant.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
