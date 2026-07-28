using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.ChangeSchoolStatus;

/// <summary>
/// PATCH /schools/{schoolId}/status — activation/suspension/blocage d'un établissement. Réservé au
/// SUPER ADMIN, comme le reste de SchoolsController (JGK-B01) : c'est le seul rôle qui voit toutes les
/// écoles à la fois.
///
/// Suspendre un établissement sans couper les sessions déjà ouvertes ne suspend rien — même
/// raisonnement que ChangeUserStatusCommandHandler (JGK-A05) : un JWT émis avant la bascule reste
/// valable jusqu'à 15 minutes, et son refresh token jusqu'à 14 jours. On révoque donc explicitement
/// TOUS les refresh tokens des utilisateurs de l'école, pas seulement ceux du Directeur.
///
/// `schools` n'est PAS une table tenant (elle définit le tenant) : un Super Admin sans SchoolId peut la
/// lire/écrire par un EF classique (voir CreateSchoolCommandHandler). `refresh_tokens`, en revanche, ne
/// se retrouve qu'en passant par les utilisateurs de l'école — et `users` EST sous RLS : la révocation
/// groupée passe donc par la fonction SECURITY DEFINER revoke_refresh_tokens_by_school (migration
/// AddSchoolStatusManagement), même porte étroite que FindActiveDirectorForSchoolAsync.
/// </summary>
public record ChangeSchoolStatusCommand(Guid SchoolId, EntityStatus Status, string Reason)
    : IRequest<ChangeSchoolStatusResult>;

public record ChangeSchoolStatusResult(
    Guid SchoolId, EntityStatus PreviousStatus, EntityStatus NewStatus, int RevokedSessions);

public class ChangeSchoolStatusCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<ChangeSchoolStatusCommandHandler> logger)
    : IRequestHandler<ChangeSchoolStatusCommand, ChangeSchoolStatusResult>
{
    public async Task<ChangeSchoolStatusResult> Handle(
        ChangeSchoolStatusCommand request, CancellationToken cancellationToken)
    {
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var school = await dbContext.Schools
            .FirstOrDefaultAsync(s => s.Id == request.SchoolId, cancellationToken)
            ?? throw new KeyNotFoundException($"Établissement {request.SchoolId} introuvable.");

        if (school.Status == request.Status)
        {
            // Réécrire le même statut ne changerait rien mais polluerait le journal d'audit d'entrées
            // vides (même garde que ChangeUserStatusCommandHandler).
            throw new ValidationException([
                new ValidationFailure(nameof(request.Status), $"L'établissement est déjà au statut {request.Status}.")
            ]);
        }

        var previousStatus = school.Status;
        var now = timeProvider.GetUtcNow();

        school.Status = request.Status;
        await dbContext.SaveChangesAsync(cancellationToken);

        // Réactiver un établissement ne coupe évidemment aucune session — il n'y a rien à révoquer.
        var revokedSessions = request.Status is EntityStatus.Active
            ? 0
            : await authStore.RevokeAllRefreshTokensForSchoolAsync(school.Id, cancellationToken);

        await auditLogStore.AppendAsync(
            school.Id, actorId, "Schools", $"ChangeStatus:{previousStatus}->{request.Status} ({request.Reason})",
            success: true, failureReason: null, currentUser.IpAddress, now, cancellationToken);

        logger.LogInformation(
            "Statut de l'établissement {SchoolId} : {Previous} -> {New} par {ActorId}. {Revoked} session(s) révoquée(s).",
            school.Id, previousStatus, request.Status, actorId, revokedSessions);

        return new ChangeSchoolStatusResult(school.Id, previousStatus, request.Status, revokedSessions);
    }
}
