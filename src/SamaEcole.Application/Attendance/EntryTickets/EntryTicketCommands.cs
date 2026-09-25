using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Attendance.EntryTickets;

/// <summary>
/// Résultat d'une acceptation ou d'une annulation de billet (Évolution N°5).
/// </summary>
public record EntryTicketActionResult(Guid TicketId, EntryTicketStatus Status, DateTimeOffset? AcceptedAt);

/// <summary>
/// POST /api/v1/billets/{id}/accept — l'ENSEIGNANT du cours visé accepte l'élève en classe (arbitrage B9). Le
/// Directeur le peut aussi. Acceptation IDEMPOTENTE : la rejouer ne change rien.
///
/// Effets : le billet passe à Accepted (qui et quand viennent du JWT et de l'horloge serveur, jamais du corps), et
/// la ligne d'appel du cours est confirmée « Retard » — y compris si l'enseignant l'avait saisie « Absent » entre-
/// temps (le statut d'avant est conservé sur le billet). Un billet accepté ne s'annule plus : une erreur se
/// corrige par un nouvel appel, pas en défaisant le billet. <see cref="IAuditableRequest"/> : historisé.
/// </summary>
public record AcceptEntryTicketCommand(Guid TicketId) : IRequest<EntryTicketActionResult>, IAuditableRequest;

/// <summary>
/// POST /api/v1/billets/{id}/cancel — la Vie Scolaire (Directeur, Surveillant) annule un billet NON encore
/// accepté ; la ligne d'appel retrouve son statut d'avant. Un billet déjà accepté est refusé (422).
/// </summary>
public record CancelEntryTicketCommand(Guid TicketId) : IRequest<EntryTicketActionResult>, IAuditableRequest;

public class AcceptEntryTicketCommandValidator : AbstractValidator<AcceptEntryTicketCommand>
{
    public AcceptEntryTicketCommandValidator() => RuleFor(x => x.TicketId).NotEmpty();
}

public class CancelEntryTicketCommandValidator : AbstractValidator<CancelEntryTicketCommand>
{
    public CancelEntryTicketCommandValidator() => RuleFor(x => x.TicketId).NotEmpty();
}

public class AcceptEntryTicketCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    IPublisher publisher,
    EntryTicketRegister register,
    TimeProvider timeProvider)
    : IRequestHandler<AcceptEntryTicketCommand, EntryTicketActionResult>
{
    public async Task<EntryTicketActionResult> Handle(AcceptEntryTicketCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        // Le filtre global et la RLS bornent à l'école courante : le billet d'une autre école est introuvable (404).
        var ticket = await dbContext.LateArrivals
            .FirstOrDefaultAsync(l => l.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(LateArrival), request.TicketId.ToString());

        if (ticket.TargetScheduleSlotId is not { } slotId || ticket.Status is null)
        {
            throw Refuse("Ce billet ne vise aucun cours : il n'y a rien à accepter en classe.");
        }

        var slot = await dbContext.ScheduleSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
            ?? throw Refuse("Le cours visé par ce billet n'existe plus.");

        await EnsureMayAcceptAsync(slot, userId, cancellationToken);

        // Idempotent : accepté une première fois, la seconde n'écrit rien (ni date, ni auteur, ni message).
        if (ticket.Status == EntryTicketStatus.Accepted)
        {
            return new EntryTicketActionResult(ticket.Id, EntryTicketStatus.Accepted, ticket.AcceptedAt);
        }

        if (ticket.Status == EntryTicketStatus.Cancelled)
        {
            throw Refuse("Ce billet a été annulé : il ne peut plus être accepté. La Vie Scolaire peut en émettre un nouveau.");
        }

        var date = DateOnly.FromDateTime(ticket.Date);

        ticket.Status = EntryTicketStatus.Accepted;
        ticket.AcceptedByUserId = userId;
        ticket.AcceptedAt = timeProvider.GetUtcNow();

        var rectification = await register.ApplyAsync(ticket, slot, date, schoolId, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);

        if (rectification is not null)
        {
            await publisher.Publish(rectification, cancellationToken);
        }

        return new EntryTicketActionResult(ticket.Id, EntryTicketStatus.Accepted, ticket.AcceptedAt);
    }

    /// <summary>
    /// Le Directeur accepte n'importe quel billet ; un Enseignant, seulement ceux du cours dont il est TITULAIRE —
    /// retrouvé depuis son COMPTE (Teacher.UserId), jamais depuis un identifiant fourni par le client (règle #10).
    /// </summary>
    private async Task EnsureMayAcceptAsync(ScheduleSlot slot, Guid userId, CancellationToken cancellationToken)
    {
        if (currentUser.Role == Role.Directeur)
        {
            return;
        }

        if (currentUser.Role != Role.Enseignant)
        {
            throw new ForbiddenException("Seuls l'enseignant du cours et le Directeur peuvent accepter un billet d'entrée.");
        }

        var teacherId = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId != slot.TeacherId)
        {
            throw new ForbiddenException(
                "Ce cours est assuré par un autre enseignant : seul lui — ou le Directeur — peut accepter ce billet.");
        }
    }

    private static ValidationException Refuse(string message)
        => new([new ValidationFailure("Ticket", message)]);
}

public class CancelEntryTicketCommandHandler(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    EntryTicketRegister register,
    TimeProvider timeProvider)
    : IRequestHandler<CancelEntryTicketCommand, EntryTicketActionResult>
{
    public async Task<EntryTicketActionResult> Handle(CancelEntryTicketCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        // Annuler est un geste de la Vie Scolaire : l'Enseignant accepte, il n'annule pas.
        if (currentUser.Role is not (Role.Directeur or Role.Surveillant))
        {
            throw new ForbiddenException("Seuls la Vie Scolaire et le Directeur peuvent annuler un billet d'entrée.");
        }

        var ticket = await dbContext.LateArrivals
            .FirstOrDefaultAsync(l => l.Id == request.TicketId, cancellationToken)
            ?? throw new NotFoundException(nameof(LateArrival), request.TicketId.ToString());

        if (ticket.TargetScheduleSlotId is not { } slotId || ticket.Status is null)
        {
            throw Refuse("Ce billet ne vise aucun cours : il n'y a rien à annuler dans le registre d'appel.");
        }

        if (ticket.Status == EntryTicketStatus.Accepted)
        {
            throw Refuse("Ce billet a déjà été accepté par l'enseignant : il ne peut plus être annulé. "
                         + "Une erreur se corrige par un nouvel appel sur la ligne de l'élève.");
        }

        // Déjà annulé : sans effet, et sans erreur — annuler deux fois ne doit pas faire échouer un double clic.
        if (ticket.Status == EntryTicketStatus.Cancelled)
        {
            return new EntryTicketActionResult(ticket.Id, EntryTicketStatus.Cancelled, null);
        }

        var slot = await dbContext.ScheduleSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken);

        ticket.Status = EntryTicketStatus.Cancelled;
        ticket.CancelledByUserId = userId;
        ticket.CancelledAt = timeProvider.GetUtcNow();

        if (slot is not null)
        {
            await register.RestoreAsync(ticket, slot, DateOnly.FromDateTime(ticket.Date), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return new EntryTicketActionResult(ticket.Id, EntryTicketStatus.Cancelled, null);
    }

    private static ValidationException Refuse(string message)
        => new([new ValidationFailure("Ticket", message)]);
}
