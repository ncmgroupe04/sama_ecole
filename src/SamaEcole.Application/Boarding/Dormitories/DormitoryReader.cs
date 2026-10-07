using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;

namespace SamaEcole.Application.Boarding.Dormitories;

/// <summary>Lecture d'un pavillon et validation du surveillant lié, partagées par les commandes de pavillon.</summary>
internal static class DormitoryReader
{
    /// <summary>
    /// Pavillon relu APRÈS écriture, avec le jeton xmin réel (même technique que RoomResult). Le nom du
    /// surveillant est celui du compte lié s'il existe, sinon le texte libre.
    /// </summary>
    public static async Task<DormitoryDto> GetDtoAsync(IApplicationDbContext dbContext, Guid id, CancellationToken cancellationToken)
    {
        var row = await dbContext.Dormitories.AsNoTracking()
            .Where(d => d.Id == id)
            .Select(d => new
            {
                d.Id, d.Name, d.Gender, d.SupervisorName, d.SupervisorPhone, d.SupervisorUserId, d.Notes,
                LinkedName = dbContext.Users.Where(u => u.Id == d.SupervisorUserId).Select(u => u.FullName).FirstOrDefault(),
                RowVersion = EF.Property<uint>(d, "xmin")
            })
            .FirstAsync(cancellationToken);

        return new DormitoryDto(
            row.Id, row.Name, row.Gender,
            row.SupervisorUserId is null ? row.SupervisorName : row.LinkedName,
            row.SupervisorPhone, row.SupervisorUserId, row.Notes, row.RowVersion);
    }

    /// <summary>Le compte lié doit être un Surveillant ACTIF de l'école courante (la RLS borne déjà `users`).</summary>
    public static async Task EnsureValidSupervisorAsync(
        IApplicationDbContext dbContext, Guid schoolId, Guid? userId, CancellationToken cancellationToken)
    {
        if (userId is null)
        {
            return;
        }

        var valid = await dbContext.Users.AsNoTracking().AnyAsync(
            u => u.Id == userId && u.SchoolId == schoolId && u.Role == Role.Surveillant && u.Status == EntityStatus.Active,
            cancellationToken);

        if (!valid)
        {
            throw new ValidationException([
                new ValidationFailure("SupervisorUserId", "Le compte indiqué n'est pas un Surveillant actif de votre établissement.")
            ]);
        }
    }
}
