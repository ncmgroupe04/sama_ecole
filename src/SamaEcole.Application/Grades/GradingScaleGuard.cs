using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades;

/// <summary>
/// Plafond de notation applicable à une note : celui du CYCLE de la classe de l'élève (système
/// hybride — Primaire /10, Collège &amp; Lycée /20), résolu par
/// <see cref="ResolveScaleForStudentAsync"/>, <see cref="ResolveScaleForGradeAsync"/> ou
/// <see cref="ResolveScaleForClassroomAsync"/> selon ce dont l'appelant dispose.
///
/// TOUTES les entrées (saisie unitaire, import) et toutes les restitutions (fiche élève, bulletin
/// PDF) passent par cette résolution. Le réglage d'école SchoolSettings.GradingScale ne gouverne
/// plus aucune note — c'est pourquoi il n'est plus lu ici.
///
/// Les seuils de MENTION ne passent pas par cette classe : ils vivent sur
/// <see cref="MentionScales.Reference"/> (/20) et sont transposés au barème d'un bulletin par
/// <see cref="MentionScales.RescaleTo"/>.
///
/// Dans tous les cas, une note hors plage est une erreur de saisie sur le bon champ (422), pas une
/// exception brute (AGENTS.md règle #9).
/// </summary>
internal static class GradingScaleGuard
{
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

    /// <summary>Plafond d'un cycle : Maternelle &amp; Primaire /10, Collège &amp; Lycée (et cycle inconnu) /20.</summary>
    public static int ScaleForCycle(CycleType? cycle)
        => cycle is { } c && c.UsesSimplifiedGrading() ? 10 : 20;

    /// <summary>
    /// Barème d'une matière donnée, cycle de la classe de l'élève compris — la résolution complète dont
    /// ont besoin la saisie et la correction d'une note (CreateGrade/UpdateGrade). Matière introuvable
    /// (impossible : l'appelant vient de la vérifier) → barème du cycle seul.
    /// </summary>
    public static async Task<decimal> ResolveMaxScoreAsync(
        IApplicationDbContext dbContext, Guid subjectId, int cycleScale, CancellationToken cancellationToken)
    {
        var maxScore = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == subjectId)
            .Select(s => s.MaxScore)
            .FirstOrDefaultAsync(cancellationToken);

        return GradeCalculator.EffectiveMaxScore(maxScore, cycleScale);
    }

    /// <summary>
    /// Contrôle partagé (Create/Update/Mention) qui rend l'erreur de saisie sur le bon champ (422). Message
    /// NEUTRE volontairement, car le barème passé peut être celui du cycle de la classe (saisie de note,
    /// via <see cref="ResolveScaleForStudentAsync"/> / <see cref="ResolveScaleForGradeAsync"/>), celui de
    /// la matière (<see cref="ResolveMaxScoreAsync"/>) ou le barème de référence des mentions
    /// (<see cref="MentionScales.Reference"/>).
    /// </summary>
    public static void EnsureWithinScale(decimal value, decimal gradingScale, string propertyName)
    {
        if (value > gradingScale)
        {
            throw new ValidationException([
                new ValidationFailure(propertyName, $"La note ne peut pas dépasser le barème ({FormatScale(gradingScale)}).")
            ]);
        }
    }

    /// <summary>« 20 » et non « 20,00 » ; « 7,5 » garde sa décimale utile. Le message part à l'utilisateur.</summary>
    internal static string FormatScale(decimal scale) =>
        scale.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
}
