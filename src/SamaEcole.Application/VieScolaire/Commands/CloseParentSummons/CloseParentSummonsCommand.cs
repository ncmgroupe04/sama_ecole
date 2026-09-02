using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.VieScolaire.Commands.CloseParentSummons;

/// <summary>
/// Consigne la suite donnée à une convocation : le parent est venu, ne s'est pas présenté, ou
/// l'entretien est reporté (Volume 1 §18.2).
///
/// Avant cette commande, une convocation n'avait ni statut ni compte rendu : le registre listait des
/// rendez-vous programmés, jamais des entretiens tenus. On ne pouvait ni distinguer une convocation
/// honorée d'une convocation ignorée, ni produire l'historique d'un élève devant l'inspection.
///
/// La suite se pose UNE SEULE FOIS, et seulement depuis <see cref="ParentSummonsStatus.Scheduled"/>.
/// Ni retour en arrière, ni correction sur place : le registre de la Vie scolaire vaut par son
/// intégrité, exactement comme le registre disciplinaire (§18.1, AGENTS.md règle #6). Une erreur se
/// corrige en émettant une NOUVELLE convocation, qui laisse les deux traces.
/// </summary>
public record CloseParentSummonsCommand(
    Guid SummonsId,
    ParentSummonsStatus Outcome,
    string? OutcomeNotes = null) : IRequest<CloseParentSummonsResult>, IAuditableRequest;

public record CloseParentSummonsResult(
    Guid SummonsId,
    ParentSummonsStatus Status,
    string? OutcomeNotes,
    DateTimeOffset ClosedAt);

public class CloseParentSummonsCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CloseParentSummonsCommand, CloseParentSummonsResult>
{
    public async Task<CloseParentSummonsResult> Handle(
        CloseParentSummonsCommand request, CancellationToken cancellationToken)
    {
        _ = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var summons = await dbContext.ParentSummons
            .FirstOrDefaultAsync(s => s.Id == request.SummonsId, cancellationToken)
            ?? throw new NotFoundException(nameof(Domain.Entities.ParentSummons), request.SummonsId);

        if (summons.Status != ParentSummonsStatus.Scheduled)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.SummonsId),
                    "La suite de cette convocation a déjà été consignée ; elle ne se réécrit pas. " +
                    "Émettez une nouvelle convocation si l'entretien doit être repris.")
            ]);
        }

        // Un compte rendu est exigé dès que l'entretien n'a PAS eu lieu comme prévu. « Honorée » se
        // suffit à elle-même — imposer un texte y produirait la phrase creuse (« RAS ») que
        // CloseCashierSessionCommand évite déjà sur les caisses qui tombent juste. Une absence ou un
        // report, eux, appellent une suite : c'est précisément ce que le registre doit conserver.
        var notes = request.OutcomeNotes?.Trim();
        if (request.Outcome != ParentSummonsStatus.Honored && string.IsNullOrWhiteSpace(notes))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.OutcomeNotes),
                    request.Outcome == ParentSummonsStatus.Missed
                        ? "Indiquez la suite donnée à cette absence (relance, nouvelle convocation…)."
                        : "Indiquez le motif du report et la suite prévue.")
            ]);
        }

        summons.Status = request.Outcome;
        summons.OutcomeNotes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        summons.ClosedAt = timeProvider.GetUtcNow();
        summons.ClosedByUserId = currentUser.UserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseParentSummonsResult(
            summons.Id, summons.Status, summons.OutcomeNotes, summons.ClosedAt.Value);
    }
}
