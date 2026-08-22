using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.ImportGradeSheet;

/// <summary>
/// TOUT le fichier est validé AVANT toute écriture, sur le MÊME code que l'aperçu (<see
/// cref="ImportGradeSheetCommand.DryRun"/>) : la moindre ligne invalide (matricule inconnu, matricule
/// d'un élève d'une autre classe, doublon dans le fichier, note hors barème) rejette l'import ENTIER en
/// 422, avec le détail ligne par ligne — jamais un import partiel qui laisserait deviner ce qui est
/// passé. Une fois validé, chaque note CRÉE ou CORRIGE (upsert) dans une seule transaction.
///
/// Un matricule n'est accepté que s'il désigne un élève réellement inscrit dans LA CLASSE VISÉE par
/// l'import (request.ClassroomId) — même contrôle que l'ancien ImportGradesCommandHandler qu'il
/// remplace : une note ne doit jamais atterrir sur l'élève d'une autre classe par erreur de fichier.
/// </summary>
public class ImportGradeSheetCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IGradeSheetImportParser fileParser)
    : IRequestHandler<ImportGradeSheetCommand, ImportGradeSheetResult>
{
    private static readonly (EvaluationType Type, string Label)[] Evaluations =
    [
        (EvaluationType.Devoir1, "Devoir 1"),
        (EvaluationType.Devoir2, "Devoir 2"),
        (EvaluationType.Composition, "Composition")
    ];

    public async Task<ImportGradeSheetResult> Handle(ImportGradeSheetCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        if (!await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.TermId), "Le trimestre indiqué n'existe pas dans votre établissement.")
            ]);
        }

        // Barème de la MATIÈRE importée (grilles APC : /40, /60, /24…) et, à défaut, celui du CYCLE de la
        // classe visée (Primaire /10, Collège & Lycée /20) — exactement la résolution de la saisie
        // unitaire, sans quoi le même fichier passerait à l'écran et échouerait à l'import.
        var cycleScale = await GradingScaleGuard.ResolveScaleForClassroomAsync(dbContext, request.ClassroomId, cancellationToken);
        var gradingScale = await GradingScaleGuard.ResolveMaxScoreAsync(dbContext, request.SubjectId, cycleScale, cancellationToken);

        // Lecture BRUTE du fichier : IGradeSheetImportParser lève déjà une ValidationException si le
        // format est illisible, l'en-tête absent, ou les colonnes ambiguës.
        var fileRows = fileParser.Parse(request.FileContent, request.FileName);

        // Le roster de la classe VISÉE, pas de l'école entière : un matricule d'un élève d'une autre
        // classe doit être rejeté, jamais silencieusement accepté.
        var roster = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => new { s.Id, s.Matricule })
            .ToListAsync(cancellationToken);

        var byMatricule = roster.ToDictionary(s => s.Matricule.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

        // Notes déjà saisies pour cette matière/trimestre, TOUTES épreuves confondues (suivies, pas
        // AsNoTracking, puisqu'on les modifie directement en cas de correction).
        var existingGrades = await dbContext.Grades
            .Where(g => g.SubjectId == request.SubjectId && g.TermId == request.TermId)
            .ToListAsync(cancellationToken);
        var existingByKey = existingGrades.ToDictionary(g => (g.StudentId, g.EvaluationType));

        var errors = new List<ValidationFailure>();
        var seenMatricules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(Guid StudentId, EvaluationType Type, decimal Value)>();

        foreach (var row in fileRows)
        {
            var field = $"Ligne {row.RowNumber}";
            var matricule = row.Matricule.Trim();

            if (matricule.Length == 0)
            {
                errors.Add(new ValidationFailure(field, "Matricule manquant."));
                continue;
            }

            if (seenMatricules.TryGetValue(matricule, out var firstRowNumber))
            {
                errors.Add(new ValidationFailure(
                    field, $"Matricule « {matricule} » déjà présent à la ligne {firstRowNumber} : doublon dans le fichier."));
                continue;
            }
            seenMatricules[matricule] = row.RowNumber;

            if (!byMatricule.TryGetValue(matricule, out var studentId))
            {
                errors.Add(new ValidationFailure(
                    field,
                    $"Matricule « {matricule} » introuvable parmi les élèves de cette classe : vérifiez le matricule et la classe sélectionnée."));
                continue;
            }

            var rawByType = new[] { row.Devoir1Raw, row.Devoir2Raw, row.CompositionRaw };

            for (var i = 0; i < Evaluations.Length; i++)
            {
                var (type, label) = Evaluations[i];
                var rawValue = rawByType[i].Trim();

                // Colonne absente du fichier, ou cellule vide : pas de note pour cette épreuve, jamais un zéro.
                if (rawValue.Length == 0)
                {
                    continue;
                }

                if (!TryParseGrade(rawValue, out var value))
                {
                    errors.Add(new ValidationFailure(
                        field, $"Note « {rawValue} » invalide pour {label} (matricule « {matricule} ») : ce n'est pas un nombre."));
                    continue;
                }

                if (value < 0)
                {
                    errors.Add(new ValidationFailure(field, $"La note de {label} ne peut pas être négative (matricule « {matricule} »)."));
                    continue;
                }

                if (value > gradingScale)
                {
                    errors.Add(new ValidationFailure(
                        field, $"La note de {label} ({value}) dépasse le barème de la matière ({GradingScaleGuard.FormatScale(gradingScale)}) pour le matricule « {matricule} »."));
                    continue;
                }

                resolved.Add((studentId, type, value));
            }
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var studentsMatched = seenMatricules.Count;

        if (request.DryRun)
        {
            // Aperçu : compte ce qui SERAIT créé/corrigé/inchangé, RIEN n'est écrit.
            var (wouldCreate, wouldUpdate, wouldUnchanged) = Tally(resolved, existingByKey, apply: false);
            return new ImportGradeSheetResult(studentsMatched, wouldCreate, wouldUpdate, wouldUnchanged);
        }

        // Fichier entièrement valide : upsert des trois épreuves dans UNE transaction (AGENTS.md règle
        // #5 — soit l'import entier est appliqué, soit rien).
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var (created, updated, unchanged) = Tally(resolved, existingByKey, apply: true);

            foreach (var (studentId, type, value) in resolved)
            {
                if (!existingByKey.ContainsKey((studentId, type)))
                {
                    dbContext.Grades.Add(new Grade
                    {
                        SchoolId = schoolId,
                        StudentId = studentId,
                        SubjectId = request.SubjectId,
                        TermId = request.TermId,
                        EvaluationType = type,
                        Value = value
                    });
                }
            }

            await dbContext.SaveChangesAsync(ct);

            return new ImportGradeSheetResult(studentsMatched, created, updated, unchanged);
        }, cancellationToken);
    }

    /// <summary>
    /// Décompte création/correction/inchangé, en appliquant réellement la correction sur l'entité
    /// SUIVIE si <paramref name="apply"/> — pour que dryRun (apply=false) obtienne le même décompte que
    /// la confirmation sans jamais modifier une entité trackée avant la transaction réelle.
    /// </summary>
    private static (int Created, int Updated, int Unchanged) Tally(
        List<(Guid StudentId, EvaluationType Type, decimal Value)> resolved,
        Dictionary<(Guid StudentId, EvaluationType Type), Grade> existingByKey,
        bool apply)
    {
        int created = 0, updated = 0, unchanged = 0;

        foreach (var (studentId, type, value) in resolved)
        {
            if (existingByKey.TryGetValue((studentId, type), out var grade))
            {
                if (grade.Value == value)
                {
                    unchanged++;
                }
                else
                {
                    if (apply) grade.Value = value;
                    updated++;
                }
            }
            else
            {
                created++;
            }
        }

        return (created, updated, unchanged);
    }

    /// <summary>Décimale FR (virgule, « 15,5 ») ou technique (point) — même tolérance des deux notations.</summary>
    private static bool TryParseGrade(string rawValue, out decimal value)
    {
        var normalized = rawValue.IndexOf(',') >= 0 && rawValue.IndexOf('.') < 0
            ? rawValue.Replace(',', '.')
            : rawValue;

        return decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }
}
