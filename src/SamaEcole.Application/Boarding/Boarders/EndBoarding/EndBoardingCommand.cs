using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Boarders.EndBoarding;

/// <summary>
/// DELETE /api/v1/boarding/unassign-bed/{boarderId}?rowVersion= — met fin au séjour : <c>IsActive = false</c>,
/// <c>EndDate</c> = aujourd'hui, lit libéré. Rien n'est supprimé (le rôle applicatif n'a plus <c>DELETE</c>) et la pension
/// déjà facturée reste due (décision #7). 409 <c>LEAVE_IN_PROGRESS</c> tant qu'une sortie est ouverte.
/// </summary>
public record EndBoardingCommand(Guid BoarderId, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class EndBoardingCommandValidator : AbstractValidator<EndBoardingCommand>
{
    public EndBoardingCommandValidator() => RuleFor(x => x.BoarderId).NotEmpty();
}

public class EndBoardingCommandHandler(IApplicationDbContext dbContext, IBoardingAssignmentService assignments)
    : IRequestHandler<EndBoardingCommand, Unit>
{
    public async Task<Unit> Handle(EndBoardingCommand request, CancellationToken cancellationToken)
    {
        var stay = await dbContext.BoardingEnrollments
            .FirstOrDefaultAsync(b => b.Id == request.BoarderId && b.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour actif {request.BoarderId} introuvable.");

        // Posé AVANT toute modification : un jeton périmé rejette l'écriture entière, jamais un état partiel.
        dbContext.SetOriginalConcurrencyToken(stay, request.RowVersion);

        await assignments.EndAsync(stay.EnrollmentId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
