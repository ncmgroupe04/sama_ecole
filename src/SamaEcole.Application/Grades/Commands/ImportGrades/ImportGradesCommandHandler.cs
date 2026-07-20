using System.Globalization;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Grades.Commands.ImportGrades;

/// <summary>
/// TOUT le fichier est validé AVANT toute écriture : la moindre ligne invalide (matricule inconnu,
/// matricule d'un élève d'une autre classe, doublon dans le fichier, note hors barème) rejette l'import
/// ENTIER en 422, avec le détail ligne par ligne — jamais un import partiel qui laisserait deviner ce
/// qui est passé. Une fois validé, chaque ligne CRÉE ou CORRIGE (upsert) dans une seule transaction :
/// un fichier corrigé après une première tentative en erreur peut être renvoyé tel quel.
///
/// Un matricule n'est accepté que s'il désigne un élève réellement inscrit dans LA CLASSE VISÉE par
/// l'import (request.ClassroomId) — même contrôle d'appartenance que SubmitAttendanceSheetCommandHandler
/// pour l'appel : une note ne doit jamais atterrir sur l'élève d'une autre classe par erreur de fichier.
/// </summary>
public class ImportGradesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IGradeImportFileParser fileParser)
    : IRequestHandler<ImportGradesCommand, ImportGradesResult>
{
    public async Task<ImportGradesResult> Handle(ImportGradesCommand request, CancellationToken cancellationToken)
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

        // Barème du CYCLE de la classe visée (Primaire /10, Collège & Lycée /20), comme la saisie unitaire
        // (CreateGradeCommandHandler) — une note d'import qui dépasse le plafond du cycle est rejetée
        // exactement de la même façon, jamais laissée passer parce qu'elle vient d'un fichier.
        var gradingScale = await GradingScaleGuard.ResolveScaleForClassroomAsync(dbContext, request.ClassroomId, cancellationToken);

        // Lecture BRUTE du fichier : IGradeImportFileParser lève déjà une ValidationException si le
        // format est illisible, l'extension non supportée, ou le fichier vide.
        var fileRows = fileParser.Parse(request.FileContent, request.FileName);

        // Le roster de la classe VISÉE, pas de l'école entière : un matricule d'un élève d'une autre
        // classe doit être rejeté, jamais silencieusement accepté (la préoccupation même du ticket).
        var roster = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => new { s.Id, s.Matricule })
            .ToListAsync(cancellationToken);

        var byMatricule = roster.ToDictionary(s => s.Matricule.Trim(), s => s.Id, StringComparer.OrdinalIgnoreCase);

        // Notes déjà saisies pour CETTE clé (matière/trimestre/type) : décide upsert (créer/corriger),
        // et suivies (pas AsNoTracking) puisqu'on les modifie directement en cas de correction.
        var existingGrades = await dbContext.Grades
            .Where(g => g.SubjectId == request.SubjectId
                        && g.TermId == request.TermId
                        && g.EvaluationType == request.EvaluationType)
            .ToDictionaryAsync(g => g.StudentId, cancellationToken);

        var errors = new List<ValidationFailure>();
        var seenMatricules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var resolved = new List<(Guid StudentId, decimal Value)>();

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

            var rawValue = row.RawValue.Trim();
            if (rawValue.Length == 0)
            {
                errors.Add(new ValidationFailure(field, $"Note manquante pour le matricule « {matricule} »."));
                continue;
            }

            if (!TryParseGrade(rawValue, out var value))
            {
                errors.Add(new ValidationFailure(field, $"Note « {rawValue} » invalide pour le matricule « {matricule} » : ce n'est pas un nombre."));
                continue;
            }

            if (value < 0)
            {
                errors.Add(new ValidationFailure(field, $"La note ne peut pas être négative (matricule « {matricule} »)."));
                continue;
            }

            if (value > gradingScale)
            {
                errors.Add(new ValidationFailure(
                    field, $"La note ({value}) dépasse le barème du cycle de la classe ({gradingScale}) pour le matricule « {matricule} »."));
                continue;
            }

            resolved.Add((studentId, value));
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        // Fichier entièrement valide : upsert de chaque ligne dans UNE transaction (AGENTS.md règle #5
        // — soit l'import entier est appliqué, soit rien).
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            int created = 0, updated = 0, unchanged = 0;

            foreach (var (studentId, value) in resolved)
            {
                if (existingGrades.TryGetValue(studentId, out var grade))
                {
                    if (grade.Value == value)
                    {
                        unchanged++;
                    }
                    else
                    {
                        grade.Value = value;
                        updated++;
                    }
                }
                else
                {
                    dbContext.Grades.Add(new Grade
                    {
                        SchoolId = schoolId,
                        StudentId = studentId,
                        SubjectId = request.SubjectId,
                        TermId = request.TermId,
                        EvaluationType = request.EvaluationType,
                        Value = value
                    });
                    created++;
                }
            }

            await dbContext.SaveChangesAsync(ct);

            return new ImportGradesResult(created, updated, unchanged);
        }, cancellationToken);
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
