using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Plafond de notation applicable à une note.
///
/// Deux résolutions coexistent volontairement :
/// <list type="bullet">
///   <item><see cref="ResolveScaleAsync"/> — barème de l'ÉCOLE (SchoolSettings.GradingScale, Volume 1
///   §8.2). Conservé tel quel : mentions, bulletin PDF (QuestPDF) et import de notes s'y appuient
///   encore.</item>
///   <item><see cref="ResolveScaleForStudentAsync"/> / <see cref="ResolveScaleForGradeAsync"/> — barème
///   du CYCLE de la classe de l'élève (système hybride : Primaire /10, Collège &amp; Lycée /20). Utilisé
///   par la SAISIE UNITAIRE d'une note (Create/Update), qui doit respecter le cycle de la classe
///   concernée et non un réglage global d'école.</item>
/// </list>
/// Dans tous les cas, une note hors plage est une erreur de saisie sur le bon champ (422), pas une
/// exception brute (AGENTS.md règle #9).
/// </summary>
internal static class GradingScaleGuard
{
    public static async Task<int> ResolveScaleAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
        => (await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken))
            ?.GradingScale ?? SchoolSettingsDefaults.GradingScale;

    /// <summary>Barème du cycle de la classe de l'élève. Élève ou classe introuvable → /20 (branche « Sinon »).</summary>
    public static async Task<int> ResolveScaleForStudentAsync(
        IApplicationDbContext dbContext, Guid studentId, CancellationToken cancellationToken)
    {
        var cycle = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Join(dbContext.Classrooms.AsNoTracking(),
                s => s.ClassroomId, c => c.Id, (s, c) => (CycleType?)c.Cycle)
            .FirstOrDefaultAsync(cancellationToken);

        return ScaleForCycle(cycle);
    }

    /// <summary>Idem à partir d'une note existante (Update ne porte que l'Id) : note → élève → classe → cycle.</summary>
    public static async Task<int> ResolveScaleForGradeAsync(
        IApplicationDbContext dbContext, Guid gradeId, CancellationToken cancellationToken)
    {
        var cycle = await dbContext.Grades.AsNoTracking()
            .Where(g => g.Id == gradeId)
            .Join(dbContext.Students.AsNoTracking(),
                g => g.StudentId, s => s.Id, (g, s) => s.ClassroomId)
            .Join(dbContext.Classrooms.AsNoTracking(),
                classroomId => classroomId, c => c.Id, (classroomId, c) => (CycleType?)c.Cycle)
            .FirstOrDefaultAsync(cancellationToken);

        return ScaleForCycle(cycle);
    }

    /// <summary>
    /// Barème du cycle d'une classe donnée. Utilisé par l'import de notes, qui vise une classe entière
    /// (cycle uniforme) — inutile de repasser par chaque élève. Classe introuvable → /20 (branche « Sinon »).
    /// </summary>
    public static async Task<int> ResolveScaleForClassroomAsync(
        IApplicationDbContext dbContext, Guid classroomId, CancellationToken cancellationToken)
    {
        var cycle = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == classroomId)
            .Select(c => (CycleType?)c.Cycle)
            .FirstOrDefaultAsync(cancellationToken);

        return ScaleForCycle(cycle);
    }

    /// <summary>Plafond d'un cycle : Primaire /10, Collège &amp; Lycée (et cycle inconnu) /20.</summary>
    public static int ScaleForCycle(CycleType? cycle)
        => cycle == CycleType.Primaire ? 10 : 20;

    /// <summary>
    /// Contrôle partagé (Create/Update/Mention) qui rend l'erreur de saisie sur le bon champ (422). Message
    /// NEUTRE volontairement, car le barème passé peut être celui de l'école (mention, via
    /// <see cref="ResolveScaleAsync"/>) comme celui du cycle de la classe (saisie de note, via
    /// <see cref="ResolveScaleForStudentAsync"/> / <see cref="ResolveScaleForGradeAsync"/>).
    /// </summary>
    public static void EnsureWithinScale(decimal value, int gradingScale, string propertyName)
    {
        if (value > gradingScale)
        {
            throw new ValidationException([
                new ValidationFailure(propertyName, $"La note ne peut pas dépasser le barème ({gradingScale}).")
            ]);
        }
    }
}
