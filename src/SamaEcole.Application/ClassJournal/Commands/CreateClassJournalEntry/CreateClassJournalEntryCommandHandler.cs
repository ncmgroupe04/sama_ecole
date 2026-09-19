using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal.Commands.CreateClassJournalEntry;

public class CreateClassJournalEntryCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ClassJournalScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<CreateClassJournalEntryCommand, ClassJournalEntryResult>
{
    public async Task<ClassJournalEntryResult> Handle(
        CreateClassJournalEntryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // La classe et la matière doivent exister DANS CETTE ÉCOLE — même garde qu'à la création
        // d'un élève (CreateStudentCommandHandler) : sans elle, une FK composite invalide
        // remonterait en 500 plutôt qu'en 422 exploitable sur le bon champ.
        var classroomExists = await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken);
        if (!classroomExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        var subjectExists = await dbContext.Subjects.AnyAsync(s => s.Id == request.SubjectId, cancellationToken);
        if (!subjectExists)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.SubjectId), "La matière indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // Année ACTIVE résolue serveur, jamais choisie par le client — même convention
        // qu'Enrollment/TeacherAssignment/AttendanceSheet.
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new BusinessRuleException("Aucune année scolaire active : impossible de journaliser une séance.");

        var teacherId = await scopeAuthorizer.EnsureCanJournalizeAsync(
            request.ClassroomId, request.SubjectId, request.SessionDate, activeYear.Id, cancellationToken);

        var entry = new ClassJournalEntry
        {
            SchoolId = schoolId,
            ClassroomId = request.ClassroomId,
            SubjectId = request.SubjectId,
            TeacherId = teacherId,
            SessionDate = request.SessionDate,
            Topic = request.Topic,
            Content = request.Content,
            Homework = request.Homework,
            HomeworkDueDate = request.HomeworkDueDate
        };

        dbContext.ClassJournalEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.ClassJournalEntries.AsNoTracking()
            .Where(e => e.Id == entry.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .FirstAsync(cancellationToken);

        return new ClassJournalEntryResult(
            entry.Id, entry.ClassroomId, entry.SubjectId, entry.TeacherId, entry.SessionDate,
            entry.Topic, entry.Content, entry.Homework, entry.HomeworkDueDate, rowVersion);
    }
}
