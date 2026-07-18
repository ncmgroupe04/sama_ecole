using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance;

/// <summary>
/// Applique la règle de saisie de l'appel (ticket JGK-D06, feuille de route Présences) :
/// l'Enseignant ne peut faire l'appel QUE pour ses classes/matières assignées ; le Directeur et le
/// Secrétariat ne sont pas bornés.
///
/// La portée s'appuie sur le lien Teacher.UserId (ajouté en JGK-D06) : le JWT ne porte que
/// l'identifiant du COMPTE, on remonte donc à la FICHE enseignant, puis à ses TeacherAssignments de
/// l'année visée. Un enseignant sans fiche rattachée, ou sans attribution pour ce couple
/// classe/matière, se voit refuser l'accès en 403 — jamais un silence qui laisserait passer la saisie.
/// </summary>
public class AttendanceScopeAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
{
    public async Task EnsureCanTakeAttendanceAsync(
        Guid classroomId, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        // Le Directeur et le Secrétariat gèrent l'appel de toutes les classes : aucune restriction.
        if (currentUser.Role is Role.Directeur or Role.Secretariat)
        {
            return;
        }

        // Toute autre absence de rôle Enseignant est déjà écartée par [Authorize] au contrôleur ; ce
        // garde-fou couvre le cas où la méthode serait appelée hors de ce contexte.
        if (currentUser.Role != Role.Enseignant)
        {
            throw new UnauthorizedAccessException("Seuls l'Enseignant, le Directeur et le Secrétariat peuvent faire l'appel.");
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        // Le compte Enseignant doit être rattaché à une FICHE (Teacher.UserId). Sans ce lien, on ne
        // peut pas savoir quelles classes lui sont assignées : refus.
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

        var isAssigned = await dbContext.TeacherAssignments.AnyAsync(
            a => a.TeacherId == teacherId
                 && a.ClassroomId == classroomId
                 && a.SubjectId == subjectId
                 && a.SchoolYearId == schoolYearId,
            cancellationToken);

        if (!isAssigned)
        {
            throw new UnauthorizedAccessException(
                "Vous n'êtes pas assigné à cette classe pour cette matière : vous ne pouvez pas en faire l'appel.");
        }
    }
}
