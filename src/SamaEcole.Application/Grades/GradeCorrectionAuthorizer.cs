using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Résout, UNE fois par requête, le contexte qui permet d'appliquer <see cref="GradeEditPolicy"/> :
/// la fenêtre de l'école, l'heure serveur, et — pour l'Enseignant — son identité et son éventuelle
/// affectation à la (classe, matière, année) visée. Partagé par la correction unitaire, l'import
/// Excel et la grille de saisie, qui n'ont ainsi qu'une seule interprétation de la règle.
///
/// Même précédent de résolution Teacher.UserId → Teacher.Id que ClassJournalScopeAuthorizer, avec une
/// différence délibérée : un Enseignant SANS fiche rattachée ne lève pas ici. Il peut encore être
/// l'AUTEUR d'une note (critère fondé sur le compte, pas sur la fiche) ; il n'est simplement affecté
/// à rien.
/// </summary>
public class GradeCorrectionAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
{
    /// <summary>Contexte d'une correction, pour une note d'un élève de <paramref name="classroomId"/>.</summary>
    public async Task<GradeCorrectionScope> ResolveAsync(
        Guid classroomId, Guid subjectId, Guid termId, CancellationToken cancellationToken)
    {
        var windowDays = await dbContext.SchoolSettings.AsNoTracking()
            .Select(s => (int?)s.GradeEditWindowDays)
            .FirstOrDefaultAsync(cancellationToken)
            ?? SchoolSettingsDefaults.GradeEditWindowDays;

        var isAssigned = false;

        if (currentUser.Role == Role.Enseignant && currentUser.UserId is { } userId)
        {
            var teacherId = await dbContext.Teachers.AsNoTracking()
                .Where(t => t.UserId == userId)
                .Select(t => (Guid?)t.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (teacherId is not null)
            {
                var schoolYearId = await dbContext.Terms.AsNoTracking()
                    .Where(t => t.Id == termId)
                    .Select(t => (Guid?)t.SchoolYearId)
                    .FirstOrDefaultAsync(cancellationToken);

                isAssigned = schoolYearId is not null
                    && await dbContext.TeacherAssignments.AnyAsync(
                        a => a.TeacherId == teacherId
                             && a.ClassroomId == classroomId
                             && a.SubjectId == subjectId
                             && a.SchoolYearId == schoolYearId,
                        cancellationToken);
            }
        }

        return new GradeCorrectionScope(
            currentUser.Role,
            currentUser.UserId?.ToString(),
            windowDays,
            timeProvider.GetUtcNow(),
            isAssigned);
    }

    /// <summary>Contexte d'une correction d'une note isolée : la classe est celle de l'élève noté.</summary>
    public async Task<GradeCorrectionScope> ResolveForGradeAsync(Grade grade, CancellationToken cancellationToken)
    {
        var classroomId = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == grade.StudentId)
            .Select(s => s.ClassroomId)
            .FirstAsync(cancellationToken);

        return await ResolveAsync(classroomId, grade.SubjectId, grade.TermId, cancellationToken);
    }
}

/// <param name="UserKey">Identifiant du compte tel que posé dans <c>Grade.CreatedBy</c> (texte), ou null.</param>
public sealed record GradeCorrectionScope(
    Role? Role, string? UserKey, int WindowDays, DateTimeOffset Now, bool IsAssigned)
{
    public bool CanCorrect(Grade grade) => CanCorrect(grade.CreatedAt, grade.CreatedBy);

    public bool CanCorrect(DateTimeOffset createdAt, string? createdBy) =>
        GradeEditPolicy.CanCorrect(
            Role, createdAt, Now, WindowDays,
            isAuthor: UserKey is not null && createdBy == UserKey,
            isAssigned: IsAssigned);

    /// <summary>
    /// 403 (<see cref="ForbiddenException"/>) : refus de rôle/propriété, jamais 409. Le message dit
    /// POURQUOI — le délai dépassé se règle avec le Directeur, la propriété avec une affectation.
    /// </summary>
    public void EnsureCanCorrect(Grade grade)
    {
        if (CanCorrect(grade)) return;

        if (Role == Domain.Enums.Role.Enseignant && !GradeEditPolicy.IsWithinWindow(grade.CreatedAt, Now, WindowDays))
        {
            throw new ForbiddenException(
                $"Le délai de {WindowDays} jour(s) pour corriger vous-même une note est dépassé : demandez au Directeur ou au Secrétariat de la corriger.");
        }

        throw new ForbiddenException(
            "Vous ne pouvez corriger que les notes que vous avez saisies, ou celles de la matière et de la classe auxquelles vous êtes affecté.");
    }
}
