using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Leaves.DeclareLeave;

/// <summary>
/// POST /api/v1/boarding/leaves — déclare la sortie d'un pensionnaire ACTIF. Une seule sortie ouverte à la fois : 409
/// <c>LEAVE_ALREADY_OPEN</c>. Un accompagnateur qui ne figure pas parmi les personnes habilitées est ACCEPTÉ et tracé
/// (<c>IsCompanionUnlisted</c>, spec Q5) : un blocage serait dangereux lors d'une urgence de santé.
/// </summary>
public record DeclareLeaveCommand(
    Guid BoarderId,
    DateOnly LeaveDate,
    DateOnly ExpectedReturnDate,
    BoardingLeaveReason Reason,
    string? ReasonDetail,
    string AccompaniedBy) : IRequest<BoardingLeaveDto>, IAuditableRequest;

public class DeclareLeaveCommandValidator : AbstractValidator<DeclareLeaveCommand>
{
    public DeclareLeaveCommandValidator()
    {
        RuleFor(x => x.BoarderId).NotEmpty();
        RuleFor(x => x.Reason).IsInEnum().WithMessage("Motif de sortie invalide.");
        RuleFor(x => x.ExpectedReturnDate).GreaterThanOrEqualTo(x => x.LeaveDate)
            .WithMessage("La date de retour prévue ne peut pas précéder la date de sortie.");
        RuleFor(x => x.AccompaniedBy).NotEmpty().WithMessage("Indiquez qui accompagne le pensionnaire.").MaximumLength(150);
        RuleFor(x => x.ReasonDetail).MaximumLength(500);
    }
}

public class DeclareLeaveCommandHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<DeclareLeaveCommand, BoardingLeaveDto>
{
    private const string AlreadyOpen = "LEAVE_ALREADY_OPEN";
    private const string AlreadyOpenMessage = "Ce pensionnaire a déjà une sortie en cours : enregistrez d'abord son retour.";

    public async Task<BoardingLeaveDto> Handle(DeclareLeaveCommand request, CancellationToken cancellationToken)
    {
        var stay = await dbContext.BoardingEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == request.BoarderId && b.IsActive, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour actif {request.BoarderId} introuvable.");

        if (await dbContext.BoardingLeaves.AnyAsync(
                l => l.BoardingEnrollmentId == stay.Id && l.ActualReturnDate == null, cancellationToken))
        {
            throw new BusinessRuleException(AlreadyOpenMessage, AlreadyOpen);
        }

        var leave = new BoardingLeave
        {
            SchoolId = stay.SchoolId,
            BoardingEnrollmentId = stay.Id,
            LeaveDate = request.LeaveDate,
            ExpectedReturnDate = request.ExpectedReturnDate,
            Reason = request.Reason,
            ReasonDetail = string.IsNullOrWhiteSpace(request.ReasonDetail) ? null : request.ReasonDetail.Trim(),
            AccompaniedBy = request.AccompaniedBy.Trim(),
            // FIGÉ ici (Q5) : la traçabilité ne change pas si la liste des personnes habilitées est modifiée plus tard.
            IsCompanionUnlisted = !CompanionMatcher.IsListed(request.AccompaniedBy, stay.AllowedExitPersons)
        };
        dbContext.BoardingLeaves.Add(leave);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex)
            when (ex.TechnicalDetail.Contains("UX_boarding_leaves_open", StringComparison.OrdinalIgnoreCase))
        {
            // Course : deux déclarations ont passé le pré-contrôle ensemble, l'index unique partiel tranche.
            throw new BusinessRuleException(AlreadyOpenMessage, AlreadyOpen);
        }

        return await LeaveReader.GetAsync(dbContext, leave.Id, Today(), cancellationToken);
    }

    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
}
