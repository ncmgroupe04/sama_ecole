using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams;

/// <summary>
/// Portée de lecture des dossiers d'examen ouverte à l'Enseignant (ticket JGK-J08, Volume 4 §22) :
/// un dossier porte des données d'état civil sensibles, donc un Enseignant ne voit que les dossiers
/// des classes où il a une affectation ACTIVE (TeacherAssignments de l'année scolaire active) —
/// Directeur et Secrétariat restent non bornés. Même mécanique que
/// <see cref="Attendance.AttendanceScopeAuthorizer"/> : le JWT ne porte que l'identifiant du COMPTE,
/// on remonte donc à la FICHE enseignant (Teacher.UserId) puis à ses TeacherAssignments.
/// </summary>
public class ExamDossierScopeAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// Classes lisibles par le compte courant, ou <c>null</c> si aucune restriction (Directeur,
    /// Secrétariat). Un Enseignant reçoit la liste — éventuellement vide — de ses classes assignées
    /// sur l'année active.
    /// </summary>
    public async Task<IReadOnlyList<Guid>?> GetReadableClassroomIdsAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Role is Role.Directeur or Role.Secretariat)
        {
            return null;
        }

        if (currentUser.Role != Role.Enseignant)
        {
            throw new UnauthorizedAccessException(
                "Seuls le Directeur, le Secrétariat, et l'Enseignant pour ses classes assignées, peuvent consulter les dossiers d'examen.");
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        var teacherId = await dbContext.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId is null)
        {
            throw new ForbiddenException(
                "Votre compte n'est rattaché à aucune fiche enseignant : demandez au Directeur de faire le rattachement.");
        }

        return await dbContext.TeacherAssignments
            .AsNoTracking()
            .Where(a => a.TeacherId == teacherId
                        && dbContext.SchoolYears.Any(y => y.Id == a.SchoolYearId && y.IsActive))
            .Select(a => a.ClassroomId)
            .Distinct()
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Refuse (403) la lecture d'une classe hors de la portée du compte courant. À appeler avant de
    /// renvoyer un dossier précis (GET /exams/dossiers/{id}) : une URL tapée directement doit renvoyer
    /// une erreur d'autorisation, jamais un silence qui laisserait deviner que le dossier existe.
    /// </summary>
    public async Task EnsureCanReadClassroomAsync(Guid classroomId, CancellationToken cancellationToken)
    {
        var readableClassroomIds = await GetReadableClassroomIdsAsync(cancellationToken);

        if (readableClassroomIds is not null && !readableClassroomIds.Contains(classroomId))
        {
            throw new ForbiddenException(
                "Vous n'êtes pas assigné à cette classe : vous ne pouvez pas consulter ce dossier.");
        }
    }
}
