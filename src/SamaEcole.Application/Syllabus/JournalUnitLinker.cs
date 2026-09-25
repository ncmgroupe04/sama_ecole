using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Institutional;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Syllabus;

/// <summary>
/// Rattache les unités du programme pointées par l'enseignant à une séance du cahier de texte (Évolution N°7). Une
/// unité doit appartenir au programme de LA matière de la séance, pour le NIVEAU de sa classe — sinon 422, jamais un
/// avancement faussé. Remplace la liste : les liens retirés sont archivés (règle #6). N'appelle pas SaveChanges.
/// </summary>
public static class JournalUnitLinker
{
    public static async Task SetAsync(
        IApplicationDbContext dbContext,
        ClassJournalEntry entry,
        IReadOnlyCollection<Guid>? unitIds,
        string actor,
        CancellationToken cancellationToken,
        string field = "SyllabusUnitIds")
    {
        if (unitIds is null)
        {
            return; // champ absent (client antérieur) : les liens existants ne bougent pas
        }

        var requested = unitIds.Distinct().ToList();
        if (requested.Count > 0)
        {
            var classroom = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == entry.ClassroomId)
                .Select(c => new { c.Name, c.Cycle })
                .FirstAsync(cancellationToken);
            var grade = AgeRules.GradeOf(classroom.Name, classroom.Cycle);

            var valid = await dbContext.SyllabusUnits.AsNoTracking()
                .Where(u => requested.Contains(u.Id) && u.SubjectId == entry.SubjectId && u.GradeLevel == grade)
                .CountAsync(cancellationToken);

            if (valid != requested.Count)
            {
                throw new ValidationException([
                    new ValidationFailure(field,
                        "Un chapitre pointé n'appartient pas au programme de cette matière pour le niveau de la classe.")
                ]);
            }
        }

        var existing = await dbContext.ClassJournalEntryUnits
            .Where(l => l.ClassJournalEntryId == entry.Id)
            .ToListAsync(cancellationToken);

        foreach (var link in existing.Where(l => !requested.Contains(l.SyllabusUnitId)))
        {
            link.SoftDelete(actor);
        }

        foreach (var unitId in requested.Where(id => existing.All(l => l.SyllabusUnitId != id)))
        {
            dbContext.ClassJournalEntryUnits.Add(new ClassJournalEntryUnit
            {
                SchoolId = entry.SchoolId,
                ClassJournalEntryId = entry.Id,
                SyllabusUnitId = unitId
            });
        }
    }
}
