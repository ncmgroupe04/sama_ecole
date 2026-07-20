using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.UpdateTeacher;

public class UpdateTeacherCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateTeacherCommand, UpdateTeacherResult>
{
    public async Task<UpdateTeacherResult> Handle(UpdateTeacherCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un enseignant d'une autre école renvoie 404, jamais une modification silencieuse.
        var teacher = await dbContext.Teachers
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.Id} introuvable.");

        // Les matières doivent exister DANS CETTE ÉCOLE — même garde qu'à la création
        // (CreateTeacherCommandHandler).
        var existingSubjectCount = await dbContext.Subjects
            .CountAsync(s => request.SubjectIds.Contains(s.Id), cancellationToken);

        if (existingSubjectCount != request.SubjectIds.Count)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.SubjectIds),
                    "Une ou plusieurs matières indiquées n'existent pas dans votre établissement.")
            ]);
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : une fiche modifiée en base depuis sa
        // lecture fait échouer SaveChangesAsync en 409, jamais un écrasement silencieux.
        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        teacher.FullName = request.FullName;
        teacher.Email = request.Email;
        teacher.Phone = request.Phone;
        teacher.BirthDate = request.BirthDate;
        teacher.BirthPlace = request.BirthPlace;
        teacher.PhotoUrl = request.PhotoUrl;

        // Réconcilie les qualifications (TeacherSubject) avec la liste soumise : retire celles qui ne
        // sont plus cochées, ajoute les nouvelles. Pas de DeleteBehavior.Cascade ici — retrait explicite,
        // cohérent avec le fait que TeacherSubject n'est jamais soft-deleté isolément.
        var currentSubjectIds = await dbContext.TeacherSubjects
            .Where(ts => ts.TeacherId == request.Id)
            .ToListAsync(cancellationToken);

        var requestedSet = request.SubjectIds.ToHashSet();

        foreach (var toRemove in currentSubjectIds.Where(ts => !requestedSet.Contains(ts.SubjectId)))
        {
            dbContext.TeacherSubjects.Remove(toRemove);
        }

        var existingSet = currentSubjectIds.Select(ts => ts.SubjectId).ToHashSet();
        foreach (var subjectId in requestedSet.Where(id => !existingSet.Contains(id)))
        {
            dbContext.TeacherSubjects.Add(new TeacherSubject
            {
                SchoolId = teacher.SchoolId,
                TeacherId = teacher.Id,
                SubjectId = subjectId
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Id == teacher.Id)
            .Select(t => EF.Property<uint>(t, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateTeacherResult(teacher.Id, newRowVersion);
    }
}
