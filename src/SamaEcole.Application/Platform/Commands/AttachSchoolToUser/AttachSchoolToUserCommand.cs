using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Platform.Commands.AttachSchoolToUser;

/// <summary>
/// POST /admin/platform/users/{userId}/schools — rattache un établissement supplémentaire à un
/// compte (groupe scolaire). RÉSERVÉ AU SUPER ADMIN.
///
/// C'est ICI que se joue le contrôle commercial du multi-établissement, et non dans un
/// [RequireFeature] sur la bascule : verrouiller la bascule elle-même enfermerait un promoteur dont
/// l'une des écoles n'est pas Premium — il basculerait vers elle sans jamais pouvoir en repartir.
/// Le rattachement est un acte d'approvisionnement, comme le crédit de SMS ; une fois accordé, la
/// bascule doit toujours fonctionner.
/// </summary>
public record AttachSchoolToUserCommand : IRequest
{
    public required Guid UserId { get; init; }
    public required Guid SchoolId { get; init; }
}

public class AttachSchoolToUserCommandHandler(
    IApplicationDbContext dbContext,
    IAuthStore authStore,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    ILogger<AttachSchoolToUserCommandHandler> logger)
    : IRequestHandler<AttachSchoolToUserCommand>
{
    public async Task Handle(AttachSchoolToUserCommand request, CancellationToken cancellationToken)
    {
        var superAdminId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        // `users` est sous RLS et le Super Admin n'a aucun tenant : lecture par la porte SECURITY
        // DEFINER du chemin d'authentification, comme partout ailleurs dans le module Platform.
        var user = await authStore.FindUserByIdAsync(request.UserId, cancellationToken)
            ?? throw new KeyNotFoundException("Compte introuvable.");

        var schoolExists = await dbContext.Schools
            .AsNoTracking()
            .AnyAsync(s => s.Id == request.SchoolId, cancellationToken);

        if (!schoolExists)
        {
            throw new KeyNotFoundException("Établissement introuvable.");
        }

        // Rattacher un compte à sa PROPRE école n'a pas de sens : elle lui est déjà accessible sans
        // aucune ligne (voir SwitchSchoolCommandHandler). Une ligne redondante ferait juste diverger
        // les deux sources.
        if (user.SchoolId == request.SchoolId)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SchoolId),
                    "Cet établissement est déjà celui du compte : aucun rattachement n'est nécessaire.")
            ]);
        }

        var alreadyAttached = await dbContext.UserSchools
            .AsNoTracking()
            .AnyAsync(us => us.UserId == request.UserId && us.SchoolId == request.SchoolId, cancellationToken);

        if (alreadyAttached)
        {
            // Idempotent : réappliquer un rattachement existant n'est pas une erreur pour l'appelant.
            return;
        }

        dbContext.UserSchools.Add(new UserSchool
        {
            UserId = request.UserId,
            SchoolId = request.SchoolId
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        await auditLogStore.AppendAsync(
            request.SchoolId, superAdminId, "Platform", "AttachSchoolToUser",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Super Admin {SuperAdminId} a rattaché l'établissement {SchoolId} au compte {UserId}.",
            superAdminId, request.SchoolId, request.UserId);
    }
}
