using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Features.Schedules;

/// <summary>
/// Applique le contrôle de propriété sur les créneaux d'emploi du temps : un Enseignant ne crée,
/// ne modifie et ne supprime un créneau que pour LUI-MÊME. Directeur, Secrétariat et Super Admin
/// construisent l'emploi du temps de tout l'établissement et ne sont pas bornés.
///
/// Même mécanique qu'<see cref="Attendance.AttendanceScopeAuthorizer"/> : le JWT ne porte que
/// l'identifiant du COMPTE, on remonte donc à la FICHE enseignant via Teacher.UserId. Le TeacherId
/// reçu dans le corps de la requête n'est jamais accepté sur parole (règle #10 d'AGENTS.md — aucune
/// donnée d'identité ne se lit dans un paramètre modifiable par le client).
///
/// Les trois verbes sont couverts, et pas seulement la création : un contrôle qui interdit de créer
/// un créneau au nom d'un collègue mais laisse le modifier ou le supprimer ne protège rien.
/// </summary>
public class ScheduleOwnershipAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// Renvoie l'identifiant de la fiche enseignant de l'utilisateur courant s'il est Enseignant,
    /// ou <c>null</c> pour les rôles non bornés (Directeur, Secrétariat, Super Admin).
    /// </summary>
    public async Task<Guid?> GetOwnTeacherIdOrNullAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant)
        {
            return null;
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        // Le compte Enseignant doit être rattaché à une FICHE (Teacher.UserId). Sans ce lien, on ne
        // peut pas savoir de qui est le créneau : refus explicite, avec l'action corrective.
        var teacherId = await dbContext.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId is null)
        {
            throw new UnauthorizedAccessException(
                "Votre compte n'est rattaché à aucune fiche enseignant : demandez au Directeur de faire le rattachement.");
        }

        return teacherId;
    }

    /// <summary>
    /// Vérifie qu'un Enseignant ne vise que sa propre fiche. <paramref name="currentSlotTeacherId"/>
    /// est le propriétaire actuel du créneau (null à la création) : il est contrôlé en plus du
    /// <paramref name="requestedTeacherId"/> pour fermer les deux angles d'attaque — s'approprier
    /// le créneau d'un collègue, et céder le sien à un collègue.
    /// </summary>
    public async Task<Guid> EnsureOwnsAsync(
        Guid requestedTeacherId,
        Guid? currentSlotTeacherId,
        CancellationToken cancellationToken)
    {
        var ownTeacherId = await GetOwnTeacherIdOrNullAsync(cancellationToken);

        if (ownTeacherId is null)
        {
            return requestedTeacherId;
        }

        if (currentSlotTeacherId is not null && currentSlotTeacherId != ownTeacherId)
        {
            throw new UnauthorizedAccessException(
                "Ce créneau appartient à un autre enseignant : vous ne pouvez pas le modifier.");
        }

        if (requestedTeacherId != ownTeacherId)
        {
            throw new UnauthorizedAccessException(
                "Vous ne pouvez créer un créneau que pour vous-même.");
        }

        return ownTeacherId.Value;
    }
}
