using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;

namespace SamaEcole.Application.Boarding.Boarders.DeleteBoarder;

/// <summary>
/// DELETE /api/v1/boarding/boarders/{id}?rowVersion= — supprime un séjour d'internat saisi par ERREUR : le séjour est archivé
/// (lit libéré) avec ses sorties et ses pointages d'appel, comme s'il n'avait jamais eu lieu. Suppression logique uniquement
/// (règle #6), rien n'est effacé. Contrairement à « Libérer » (<see cref="EndBoarding.EndBoardingCommand"/>), l'historique du séjour
/// disparaît des listes. La pension déjà facturée reste due (décision #7) : les frais de l'inscription ne sont pas touchés.
/// </summary>
public record DeleteBoarderCommand(Guid BoarderId, uint RowVersion) : IRequest<Unit>, IAuditableRequest;

public class DeleteBoarderCommandValidator : AbstractValidator<DeleteBoarderCommand>
{
    public DeleteBoarderCommandValidator() => RuleFor(x => x.BoarderId).NotEmpty();
}

public class DeleteBoarderCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteBoarderCommand, Unit>
{
    public async Task<Unit> Handle(DeleteBoarderCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la RLS bornent la recherche à l'école courante : un séjour d'une autre école renvoie 404.
        var stay = await dbContext.BoardingEnrollments
            .FirstOrDefaultAsync(b => b.Id == request.BoarderId, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour {request.BoarderId} introuvable.");

        // Posé AVANT toute modification : un jeton périmé rejette l'écriture entière (409), jamais un état partiel.
        dbContext.SetOriginalConcurrencyToken(stay, request.RowVersion);

        await EnrollmentLinkRelease.ArchiveStaysAsync(dbContext, [stay], actorId.ToString(), cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
