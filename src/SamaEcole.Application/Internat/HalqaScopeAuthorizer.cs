using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat;

/// <summary>
/// Portée d'accès à une Halqa : un Oustaz ne lit ni n'écrit que le suivi des élèves rattachés à SA Halqa
/// (<see cref="Domain.Entities.Student.InstructorId"/>). Le Directeur, le Secrétariat et le Surveillant ne sont pas
/// bornés — l'écriture reste réservée par rôle côté contrôleur.
///
/// L'Oustaz se connecte avec le rôle Enseignant (aucun rôle dédié : un nouveau rôle toucherait le JWT, les
/// contraintes de la table users et les gardes de rôle en base) ; c'est <see cref="Domain.Entities.Instructor.UserId"/>
/// qui le distingue d'un enseignant du cursus. Même précédent que ClassJournalScopeAuthorizer
/// (Teacher.UserId → Teacher.Id).
/// </summary>
public class HalqaScopeAuthorizer(IApplicationDbContext dbContext, ICurrentUserService currentUser)
{
    /// <summary>
    /// Fiche d'Oustaz du compte courant, ou <c>null</c> pour un rôle NON BORNÉ. <c>null</c> signifie toujours « ce
    /// rôle n'est pas restreint à une Halqa » : un Enseignant sans fiche d'Oustaz lève plutôt que de renvoyer
    /// <c>null</c>, pour ne jamais confondre les deux cas.
    /// </summary>
    public async Task<Guid?> GetOwnInstructorIdOrNullAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant) return null;

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        var own = await dbContext.Instructors
            .AsNoTracking()
            .Where(i => i.UserId == userId)
            .Select(i => new { i.Id, i.Status })
            .FirstOrDefaultAsync(cancellationToken);

        if (own is null)
        {
            throw new ForbiddenException(
                "Votre compte n'est rattaché à aucune fiche d'Oustaz : demandez au Directeur de faire le rattachement.");
        }

        if (own.Status != EntityStatus.Active)
        {
            throw new ForbiddenException(
                "Votre fiche d'Oustaz n'est pas active : demandez au Directeur de la réactiver.");
        }

        return own.Id;
    }

    /// <summary>L'Oustaz ne consulte que SA Halqa ; un rôle non borné consulte toutes les Halqa.</summary>
    public async Task EnsureCanAccessHalqaAsync(Guid instructorId, CancellationToken cancellationToken)
    {
        var own = await GetOwnInstructorIdOrNullAsync(cancellationToken);
        if (own is not null && own != instructorId)
        {
            throw new ForbiddenException("Cette Halqa n'est pas la vôtre : vous ne pouvez consulter que vos propres élèves.");
        }
    }

    /// <summary>
    /// Vérifie qu'un élève est dans la Halqa de l'Oustaz connecté. Un élève sans Oustaz n'est dans la Halqa de
    /// personne : seul le Directeur peut alors saisir son suivi, après l'avoir rattaché.
    /// </summary>
    public async Task EnsureCanAccessStudentAsync(Guid? studentInstructorId, CancellationToken cancellationToken)
    {
        var own = await GetOwnInstructorIdOrNullAsync(cancellationToken);
        if (own is not null && own != studentInstructorId)
        {
            throw new ForbiddenException(
                "Cet élève n'est pas dans votre Halqa : demandez au Directeur de vous le rattacher.");
        }
    }
}
