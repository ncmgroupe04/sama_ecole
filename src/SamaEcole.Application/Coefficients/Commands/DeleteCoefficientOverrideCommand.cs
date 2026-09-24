using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Coefficients.Commands;

/// <summary>
/// DELETE /api/v1/coefficients/{id} — « Rétablir » : la matière retrouve la valeur de la portée
/// supérieure (série, puis matière). Suppression LOGIQUE (règle #6) : la clé redevient libre grâce aux
/// index uniques partiels. Verrou optimiste comme DeleteSubjectCommand (règle #5).
/// </summary>
public record DeleteCoefficientOverrideCommand(Guid Id, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class DeleteCoefficientOverrideCommandValidator : AbstractValidator<DeleteCoefficientOverrideCommand>
{
    public DeleteCoefficientOverrideCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

public class DeleteCoefficientOverrideCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteCoefficientOverrideCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCoefficientOverrideCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : viser la surcharge d'une
        // autre école renvoie 404, jamais une suppression silencieuse.
        var row = await dbContext.SubjectCoefficientOverrides
            .FirstOrDefaultAsync(o => o.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Surcharge de coefficient {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(row, request.RowVersion);
        row.SoftDelete(actorId.ToString());

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
