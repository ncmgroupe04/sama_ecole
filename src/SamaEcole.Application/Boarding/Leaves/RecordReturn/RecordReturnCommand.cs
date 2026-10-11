using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Boarding.Leaves.RecordReturn;

/// <summary>
/// PUT /api/v1/boarding/leaves/{id}/return — pose la date de retour réelle (aujourd'hui par défaut ; jamais dans le futur ni
/// avant la sortie). Un retour déjà enregistré est refusé en 409 <c>LEAVE_ALREADY_RETURNED</c> (pas d'idempotence
/// silencieuse : deux opérateurs ne doivent pas croire avoir tous deux saisi le retour). <paramref name="RowVersion"/> :
/// verrouillage optimiste (règle #5).
/// </summary>
public record RecordReturnCommand(Guid LeaveId, uint RowVersion, DateOnly? ActualReturnDate)
    : IRequest<BoardingLeaveDto>, IAuditableRequest;

public class RecordReturnCommandValidator : AbstractValidator<RecordReturnCommand>
{
    public RecordReturnCommandValidator() => RuleFor(x => x.LeaveId).NotEmpty();
}

public class RecordReturnCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<RecordReturnCommand, BoardingLeaveDto>
{
    public async Task<BoardingLeaveDto> Handle(RecordReturnCommand request, CancellationToken cancellationToken)
    {
        var leave = await dbContext.BoardingLeaves.FirstOrDefaultAsync(l => l.Id == request.LeaveId, cancellationToken)
            ?? throw new KeyNotFoundException($"Sortie {request.LeaveId} introuvable.");

        if (leave.ActualReturnDate is not null)
        {
            throw new BusinessRuleException("Le retour de cette sortie est déjà enregistré.", "LEAVE_ALREADY_RETURNED");
        }

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var returnDate = request.ActualReturnDate ?? today;

        if (returnDate > today)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(request.ActualReturnDate), "La date de retour ne peut pas être dans le futur.")]);
        }

        if (returnDate < leave.LeaveDate)
        {
            throw new ValidationException(
                [new ValidationFailure(nameof(request.ActualReturnDate), "La date de retour ne peut pas précéder la date de sortie.")]);
        }

        // Posé AVANT toute modification : un jeton périmé rejette l'écriture entière, jamais un état partiel.
        dbContext.SetOriginalConcurrencyToken(leave, request.RowVersion);
        leave.ActualReturnDate = returnDate;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await LeaveReader.GetAsync(dbContext, leave.Id, today, cancellationToken);
    }
}
