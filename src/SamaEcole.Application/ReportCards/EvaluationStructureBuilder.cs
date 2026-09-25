using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Construit la grille d'évaluation imprimée d'un bulletin à partir de la STRUCTURE configurée par
/// l'école (Subject.ParentSubjectId / MaxScore / DisplayOrder / Column1Header / Column2Header), et non
/// à partir des notes saisies.
///
/// La différence est tout l'intérêt de la chose : les modèles officiels du primaire impriment la grille
/// ENTIÈRE, cases vides comprises — un bulletin dont la moitié des activités ne sont pas encore notées
/// doit montrer ces lignes vides, là où le tableau du secondaire ne liste que les matières notées. Un
/// tableau reconstruit depuis les seules notes existantes changerait de forme d'un élève à l'autre.
///
/// Renvoie NULL quand le niveau ne déclare aucune hiérarchie : le bulletin retombe alors sur ses
/// tableaux d'origine, strictement inchangés. C'est l'état de toutes les données antérieures à cette
/// option, donc de l'immense majorité des bulletins.
/// </summary>
internal static class EvaluationStructureBuilder
{
    /// <summary>Entêtes du modèle CI-CP, retenus tant que l'école n'a rien saisi de plus précis.</summary>
    private const string DefaultColumn1Header = "Domaines";
    private const string DefaultColumn2Header = "Activités";

    /// <summary>
    /// <paramref name="classroomLevel"/> est le niveau TEXTUEL de la classe (Classroom.Level), rapproché
    /// de celui des matières (Subject.Level) — les deux suivent la même nomenclature libre par école, et
    /// c'est déjà ainsi que le reste du produit relie une classe à ses matières. Aucune clé étrangère
    /// n'est introduite ici : AGENTS.md interdit explicitement d'ériger les niveaux en table.
    ///
    /// <paramref name="gradingScale"/> sert de barème de repli aux lignes qui ne fixent pas le leur, et
    /// <paramref name="mentionsOnReferenceScale"/> porte les mentions de l'école (/20) dont découle
    /// l'appréciation de chaque ligne, calculée sur son pourcentage de réussite.
    /// </summary>
    public static async Task<EvaluationStructureDto?> BuildAsync(
        IApplicationDbContext dbContext,
        string classroomLevel,
        int gradingScale,
        IReadOnlyList<SubjectGradeDto> gradedSubjects,
        IReadOnlyList<(string Label, decimal MinAverage)> mentionsOnReferenceScale,
        IReadOnlySet<Guid> exemptSubjectIds,
        CancellationToken cancellationToken)
    {
        // Toutes les matières de l'école (quelques dizaines au plus), filtrées EN MÉMOIRE sur le niveau :
        // la comparaison doit tolérer la casse et les espaces (« Primaire » vs « primaire  »), ce qu'un
        // WHERE SQL sur texte libre ferait mal et de façon dépendante du collationnement de la base.
        var all = await dbContext.Subjects.AsNoTracking()
            .Select(s => new StructureRow(
                s.Id, s.Name, s.Level, s.ParentSubjectId, s.MaxScore, s.DisplayOrder, s.Column1Header, s.Column2Header))
            .ToListAsync(cancellationToken);

        var levelSubjects = all
            .Where(s => string.Equals(s.Level.Trim(), classroomLevel.Trim(), StringComparison.OrdinalIgnoreCase))
            .ToList();

        // Aucune hiérarchie déclarée à ce niveau : rien à composer, le bulletin garde ses tableaux d'origine.
        if (!levelSubjects.Exists(s => s.ParentSubjectId is not null))
        {
            return null;
        }

        var scoresBySubject = gradedSubjects.ToDictionary(s => s.SubjectId, s => s.Average);

        var childrenByParent = levelSubjects
            .Where(s => s.ParentSubjectId is not null)
            .GroupBy(s => s.ParentSubjectId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.CurrentCulture).ToList());

        var groups = new List<EvaluationGroupDto>();

        foreach (var root in levelSubjects
                     .Where(s => s.ParentSubjectId is null)
                     .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.CurrentCulture))
        {
            var children = childrenByParent.GetValueOrDefault(root.Id);

            // Domaine sans activité : la matière s'imprime seule sur une ligne, sans fusion de cellule —
            // la rétrocompatibilité demandée pour les matières simples d'une grille par ailleurs
            // hiérarchique. Label null signale au document qu'il n'y a pas de seconde colonne à remplir.
            var lines = children is null or { Count: 0 }
                ? [BuildLine(root, label: null, scoresBySubject, gradingScale, mentionsOnReferenceScale, exemptSubjectIds)]
                : children.ConvertAll(child =>
                    BuildLine(child, child.Name, scoresBySubject, gradingScale, mentionsOnReferenceScale, exemptSubjectIds));

            groups.Add(new EvaluationGroupDto(root.Id, root.Name, lines));
        }

        // Filet de sécurité : une activité dont le domaine est introuvable à ce niveau (archivé à la main
        // en base, déplacé hors du niveau) n'a aucun groupe où s'afficher. Elle est imprimée SEULE plutôt
        // que passée sous silence : un bulletin est un document officiel, une ligne notée n'en disparaît
        // pas parce que la structure a dérivé. Les commandes interdisent d'en créer une (le domaine visé
        // doit exister et être visible) — ce cas ne devrait jamais survenir.
        var knownRootIds = levelSubjects.Where(s => s.ParentSubjectId is null).Select(s => s.Id).ToHashSet();

        foreach (var orphan in levelSubjects
                     .Where(s => s.ParentSubjectId is { } p && !knownRootIds.Contains(p))
                     .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.CurrentCulture))
        {
            groups.Add(new EvaluationGroupDto(
                orphan.Id,
                orphan.Name,
                [BuildLine(orphan, label: null, scoresBySubject, gradingScale, mentionsOnReferenceScale, exemptSubjectIds)]));
        }

        // Les entêtes qualifient la GRILLE : on retient ceux du premier domaine qui en déclare, dans
        // l'ordre d'affichage — l'écran de configuration ne les propose d'ailleurs que sur les domaines.
        var headerSource = levelSubjects
            .Where(s => s.ParentSubjectId is null)
            .OrderBy(s => s.DisplayOrder).ThenBy(s => s.Name, StringComparer.CurrentCulture)
            .ToList();

        var column1 = headerSource.Find(s => !string.IsNullOrWhiteSpace(s.Column1Header))?.Column1Header;
        var column2 = headerSource.Find(s => !string.IsNullOrWhiteSpace(s.Column2Header))?.Column2Header;

        return new EvaluationStructureDto(
            column1 ?? DefaultColumn1Header,
            column2 ?? DefaultColumn2Header,
            groups);
    }

    private static EvaluationLineDto BuildLine(
        StructureRow subject,
        string? label,
        IReadOnlyDictionary<Guid, decimal> scoresBySubject,
        int gradingScale,
        IReadOnlyList<(string Label, decimal MinAverage)> mentionsOnReferenceScale,
        IReadOnlySet<Guid> exemptSubjectIds)
    {
        // Pas de note saisie → null, et non zéro : la case reste vide sur le bulletin.
        var score = scoresBySubject.TryGetValue(subject.Id, out var value) ? value : (decimal?)null;
        var maxScore = GradeCalculator.EffectiveMaxScore(subject.MaxScore, gradingScale);

        return new EvaluationLineDto(
            subject.Id,
            label,
            score,
            maxScore,
            GradeCalculator.AppreciationFor(score, maxScore, mentionsOnReferenceScale),
            exemptSubjectIds.Contains(subject.Id));
    }

    /// <summary>Projection minimale d'une <see cref="Subject"/> — seuls les champs de structure sont lus.</summary>
    private sealed record StructureRow(
        Guid Id,
        string Name,
        string Level,
        Guid? ParentSubjectId,
        decimal? MaxScore,
        int DisplayOrder,
        string? Column1Header,
        string? Column2Header);
}
