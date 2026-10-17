using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.RollBook;

/// <summary>
/// Portée du cahier de roulement : le Directeur, le Secrétariat, le Surveillant et le Super Admin voient et
/// saisissent toutes les classes ; un Enseignant seulement celles où il est AFFECTÉ cette année, pour n'importe
/// quelle matière (le cahier est celui de la classe, pas d'un cours — contrairement à la feuille d'appel).
/// </summary>
public sealed class RollBookScopeAuthorizer(IApplicationDbContext dbContext, ICurrentUserService currentUser)
{
    public async Task EnsureCanAccessClassroomAsync(Guid classroomId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant)
        {
            return;
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        var teacherId = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId is null)
        {
            throw new ForbiddenException(
                "Votre compte n'est rattaché à aucune fiche enseignant : demandez au Directeur de faire le rattachement.");
        }

        var isAssigned = await dbContext.TeacherAssignments.AnyAsync(
            a => a.TeacherId == teacherId && a.ClassroomId == classroomId && a.SchoolYearId == schoolYearId,
            cancellationToken);

        if (!isAssigned)
        {
            throw new ForbiddenException(
                "Vous n'êtes affecté à aucune matière de cette classe : vous ne pouvez pas en consulter ni en tenir le cahier.");
        }
    }
}
