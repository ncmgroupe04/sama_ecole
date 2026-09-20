using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers;

/// <summary>
/// Garde partagée par CreateTeacherCommandHandler et LinkTeacherUserAccountCommandHandler (ticket
/// JGK-D06) : un <c>UserId</c> rattaché à une fiche enseignant doit désigner un compte Enseignant DE
/// CETTE école, pas déjà rattaché à une autre fiche. Factorisée pour ne pas dupliquer la même règle de
/// sécurité dans les deux handlers.
/// </summary>
internal static class TeacherUserAccountLink
{
    public static async Task EnsureLinkableAsync(
        IApplicationDbContext dbContext,
        Guid schoolId,
        Guid userId,
        Guid? excludeTeacherId,
        CancellationToken cancellationToken)
    {
        // AsNoTracking : ce compte est seulement inspecté (rôle, unicité du rattachement) ici, jamais
        // modifié — la fiche enseignant référence son Id, pas l'entité même.
        var user = await dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId && u.SchoolId == schoolId, cancellationToken);

        if (user is null)
        {
            throw new ValidationException([
                new ValidationFailure("UserId", "Le compte indiqué n'existe pas dans votre établissement.")
            ]);
        }

        if (user.Role != Role.Enseignant)
        {
            throw new ValidationException([
                new ValidationFailure("UserId", "Seul un compte de rôle Enseignant peut être rattaché à une fiche enseignant.")
            ]);
        }

        var alreadyLinked = await dbContext.Teachers
            .AnyAsync(t => t.UserId == userId && (excludeTeacherId == null || t.Id != excludeTeacherId), cancellationToken);

        if (alreadyLinked)
        {
            throw new ValidationException([
                new ValidationFailure("UserId", "Ce compte est déjà rattaché à une autre fiche enseignant.")
            ]);
        }
    }
}
