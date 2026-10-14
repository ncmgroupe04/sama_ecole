using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>Règles de saisie communes à la création et à la modification d'une session.</summary>
internal static class AttendanceSessionRules
{
    public const int MaxName = 60;
    public const int MaxTimeLabel = 40;
    public const int MaxSessions = 30;

    public static void Apply<T>(
        AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, string>> name,
        System.Linq.Expressions.Expression<Func<T, AttendanceAudience>> audience,
        System.Linq.Expressions.Expression<Func<T, IReadOnlyList<int>>> days,
        System.Linq.Expressions.Expression<Func<T, string?>> timeLabel)
    {
        validator.RuleFor(name).NotEmpty().WithMessage("Donnez un nom à la session.")
            .MaximumLength(MaxName).WithMessage($"Le nom ne doit pas dépasser {MaxName} caractères.");
        validator.RuleFor(audience).IsInEnum();
        validator.RuleFor(days).NotEmpty().WithMessage("Choisissez au moins un jour.")
            .Must(d => d is not null && d.All(x => x is >= 1 and <= 7)).WithMessage("Les jours vont de 1 (lundi) à 7 (dimanche).");
        validator.RuleFor(timeLabel).MaximumLength(MaxTimeLabel).WithMessage($"Le repère horaire ne doit pas dépasser {MaxTimeLabel} caractères.");
    }

    public static string? CleanLabel(string? label) => string.IsNullOrWhiteSpace(label) ? null : label.Trim();

    /// <summary>Un nom par école parmi les sessions vivantes ; la comparaison ignore la casse.</summary>
    public static async Task EnsureNameFreeAsync(
        IApplicationDbContext dbContext, string name, Guid? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        var taken = await dbContext.AttendanceSessions.AsNoTracking()
            .AnyAsync(s => s.Name.ToLower() == lowered && s.Id != exceptId, cancellationToken);
        if (taken)
        {
            throw new ValidationException([new ValidationFailure("Name", "Une session porte déjà ce nom.")]);
        }
    }
}

// ---------------------------------------------------------------------------------------------------------------- création

/// <summary>POST /api/v1/boarding/attendance/sessions — crée une session de pointage (Directeur).</summary>
public record CreateAttendanceSessionCommand(
    string Name, AttendanceAudience Audience, IReadOnlyList<int> Days, string? TimeLabel, bool IsActive = true)
    : IRequest<AttendanceSessionDto>, IAuditableRequest;

public class CreateAttendanceSessionCommandValidator : AbstractValidator<CreateAttendanceSessionCommand>
{
    public CreateAttendanceSessionCommandValidator() =>
        AttendanceSessionRules.Apply(this, x => x.Name, x => x.Audience, x => x.Days, x => x.TimeLabel);
}

public class CreateAttendanceSessionCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateAttendanceSessionCommand, AttendanceSessionDto>
{
    public async Task<AttendanceSessionDto> Handle(CreateAttendanceSessionCommand request, CancellationToken cancellationToken)
    {
        var name = request.Name.Trim();
        await AttendanceSessionRules.EnsureNameFreeAsync(dbContext, name, null, cancellationToken);

        var existing = await dbContext.AttendanceSessions.AsNoTracking().CountAsync(cancellationToken);
        if (existing >= AttendanceSessionRules.MaxSessions)
        {
            throw new BusinessRuleException(
                $"{AttendanceSessionRules.MaxSessions} sessions au plus : supprimez ou fusionnez celles qui ne servent plus.", "ATTENDANCE_SESSION_LIMIT");
        }

        var nextOrder = existing == 0 ? 0 : await dbContext.AttendanceSessions.AsNoTracking().MaxAsync(s => s.SortOrder, cancellationToken) + 1;

        var session = new AttendanceSession
        {
            SchoolId = tenantProvider.CurrentSchoolId
                ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant."),
            Name = name,
            Audience = request.Audience,
            Days = AttendanceDays.ToMask(request.Days),
            TimeLabel = AttendanceSessionRules.CleanLabel(request.TimeLabel),
            IsActive = request.IsActive,
            SortOrder = nextOrder
        };
        dbContext.AttendanceSessions.Add(session);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex) when (ex.TechnicalDetail.Contains("UX_attendance_sessions_name", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("Name", "Une session porte déjà ce nom.")]);
        }

        return AttendanceSessionMapper.ToDto(session, false);
    }
}

// ------------------------------------------------------------------------------------------------------------ modification

/// <summary>
/// PUT /api/v1/boarding/attendance/sessions/{id}. L'audience n'est plus modifiable dès qu'un pointage existe : elle décide de la
/// liste d'élèves de la feuille, la changer rendrait les pointages passés incohérents.
/// </summary>
public record UpdateAttendanceSessionCommand(
    Guid Id, string Name, AttendanceAudience Audience, IReadOnlyList<int> Days, string? TimeLabel, bool IsActive)
    : IRequest<AttendanceSessionDto>, IAuditableRequest;

public class UpdateAttendanceSessionCommandValidator : AbstractValidator<UpdateAttendanceSessionCommand>
{
    public UpdateAttendanceSessionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        AttendanceSessionRules.Apply(this, x => x.Name, x => x.Audience, x => x.Days, x => x.TimeLabel);
    }
}

public class UpdateAttendanceSessionCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateAttendanceSessionCommand, AttendanceSessionDto>
{
    public async Task<AttendanceSessionDto> Handle(UpdateAttendanceSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await dbContext.AttendanceSessions.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Session de pointage {request.Id} introuvable.");

        var name = request.Name.Trim();
        await AttendanceSessionRules.EnsureNameFreeAsync(dbContext, name, session.Id, cancellationToken);

        var hasRecords = await dbContext.BoardingAttendances.AsNoTracking().AnyAsync(a => a.SessionId == session.Id, cancellationToken);
        if (hasRecords && request.Audience != session.Audience)
        {
            throw new BusinessRuleException(
                "Des pointages existent déjà pour cette session : son public ne peut plus changer. Créez une nouvelle session, et désactivez celle-ci.",
                "ATTENDANCE_SESSION_AUDIENCE_LOCKED");
        }

        session.Name = name;
        session.Audience = request.Audience;
        session.Days = AttendanceDays.ToMask(request.Days);
        session.TimeLabel = AttendanceSessionRules.CleanLabel(request.TimeLabel);
        session.IsActive = request.IsActive;

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DuplicateRecordException ex) when (ex.TechnicalDetail.Contains("UX_attendance_sessions_name", StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException([new ValidationFailure("Name", "Une session porte déjà ce nom.")]);
        }

        return AttendanceSessionMapper.ToDto(session, hasRecords);
    }
}

// -------------------------------------------------------------------------------------------------------------- suppression

/// <summary>
/// DELETE /api/v1/boarding/attendance/sessions/{id} — suppression logique d'une session JAMAIS utilisée. Une session qui porte des
/// pointages ne se supprime pas (le registre et les récapitulatifs en dépendent) : on la désactive.
/// </summary>
public record DeleteAttendanceSessionCommand(Guid Id) : IRequest, IAuditableRequest;

public class DeleteAttendanceSessionCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<DeleteAttendanceSessionCommand>
{
    public async Task Handle(DeleteAttendanceSessionCommand request, CancellationToken cancellationToken)
    {
        var session = await dbContext.AttendanceSessions.FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Session de pointage {request.Id} introuvable.");

        var hasRecords = await dbContext.BoardingAttendances.AsNoTracking().AnyAsync(a => a.SessionId == session.Id, cancellationToken);
        if (hasRecords)
        {
            throw new BusinessRuleException(
                "Des pointages existent pour cette session : désactivez-la plutôt que la supprimer.", SoftDeleteLifecycle.ResourceInUse);
        }

        var actorId = currentUser.UserId ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");
        session.SoftDelete(actorId.ToString());
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

// ------------------------------------------------------------------------------------------------------------------ ordre

/// <summary>PUT /api/v1/boarding/attendance/sessions/order — l'ordre d'affichage voulu : la liste COMPLÈTE des sessions, dans l'ordre.</summary>
public record ReorderAttendanceSessionsCommand(IReadOnlyList<Guid> OrderedIds) : IRequest;

public class ReorderAttendanceSessionsCommandValidator : AbstractValidator<ReorderAttendanceSessionsCommand>
{
    public ReorderAttendanceSessionsCommandValidator() =>
        RuleFor(x => x.OrderedIds).NotEmpty().WithMessage("La liste des sessions est vide.")
            .Must(ids => ids is not null && ids.Distinct().Count() == ids.Count).WithMessage("Une session figure plusieurs fois.");
}

public class ReorderAttendanceSessionsCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ReorderAttendanceSessionsCommand>
{
    public async Task Handle(ReorderAttendanceSessionsCommand request, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.AttendanceSessions.ToListAsync(cancellationToken);

        if (sessions.Count != request.OrderedIds.Count || sessions.Any(s => !request.OrderedIds.Contains(s.Id)))
        {
            throw new ValidationException([new ValidationFailure("OrderedIds", "La liste ne correspond plus aux sessions de l'école : rechargez l'écran.")]);
        }

        for (var position = 0; position < request.OrderedIds.Count; position++)
        {
            sessions.First(s => s.Id == request.OrderedIds[position]).SortOrder = position;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
