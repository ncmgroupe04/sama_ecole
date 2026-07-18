using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Attendance.Queries.InitializeAttendanceSheet;

public class InitializeAttendanceSheetQueryHandler(
    IApplicationDbContext dbContext,
    AttendanceScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<InitializeAttendanceSheetQuery, AttendanceRosterDto>
{
    public async Task<AttendanceRosterDto> Handle(InitializeAttendanceSheetQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent tout à l'école courante : une classe/matière d'une
        // autre école est simplement introuvable, jamais exposée.
        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == request.ClassroomId)
            .Select(c => new { c.Id, c.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);

        var subject = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == request.SubjectId)
            .Select(s => new { s.Id, s.Name })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);

        // L'appel se rattache à l'année ACTIVE (comme la soumission) — c'est aussi elle qui sert à
        // vérifier la portée de l'enseignant. Sans année active, l'appel n'a rien à quoi se rattacher.
        var activeYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure("SchoolYear", "Aucune année scolaire active. Activez une année scolaire avant de faire l'appel.")
            ]);

        // Portée : un Enseignant ne peut ouvrir la grille que pour ses classes/matières assignées
        // (403 sinon). Directeur/Secrétariat non bornés.
        await scopeAuthorizer.EnsureCanTakeAttendanceAsync(request.ClassroomId, request.SubjectId, activeYear.Id, cancellationToken);

        var students = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .OrderBy(s => s.FullName)
            .Select(s => new { s.Id, s.Matricule, s.FullName })
            .ToListAsync(cancellationToken);

        // Fiche déjà saisie pour cette clé ? On récupère les statuts pour pré-remplir la grille.
        var existingSheetId = await dbContext.AttendanceSheets.AsNoTracking()
            .Where(a => a.ClassroomId == request.ClassroomId
                        && a.SubjectId == request.SubjectId
                        && a.Date == request.Date
                        && a.Period == request.Period)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Requête exécutée même sans fiche existante : le prédicat « != null » la rend simplement
        // vide dans ce cas, ce qui évite d'avoir à nommer le type anonyme pour un dictionnaire vide.
        var existingStatuses = await dbContext.StudentAttendances.AsNoTracking()
            .Where(sa => existingSheetId != null && sa.AttendanceSheetId == existingSheetId)
            .ToDictionaryAsync(sa => sa.StudentId, sa => new { sa.Status, sa.LateMinutes }, cancellationToken);

        var rows = students
            .Select(s =>
            {
                existingStatuses.TryGetValue(s.Id, out var recorded);
                return new AttendanceRosterRow(
                    s.Id,
                    s.Matricule,
                    s.FullName,
                    recorded?.Status.ToString(),
                    recorded?.LateMinutes ?? 0);
            })
            .ToList();

        return new AttendanceRosterDto(
            classroom.Id,
            classroom.Name,
            subject.Id,
            subject.Name,
            request.Date,
            request.Period,
            existingSheetId is not null,
            rows);
    }
}
