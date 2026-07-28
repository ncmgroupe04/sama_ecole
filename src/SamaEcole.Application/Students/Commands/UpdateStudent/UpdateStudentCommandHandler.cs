using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.UpdateStudent;

public class UpdateStudentCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateStudentCommand, UpdateStudentResult>
{
    public async Task<UpdateStudentResult> Handle(UpdateStudentCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // un élève d'une autre école renvoie 404, jamais une modification silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.Id} introuvable.");

        // La classe doit exister DANS CETTE ÉCOLE — même garde qu'à la création (CreateStudentCommandHandler).
        var classroomExists = await dbContext.Classrooms
            .AnyAsync(c => c.Id == request.ClassroomId, cancellationToken);

        if (!classroomExists)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.ClassroomId),
                    "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : une fiche modifiée en base depuis sa lecture
        // fait échouer SaveChangesAsync en 409, jamais un écrasement silencieux.
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        student.FullName = request.FullName.ToTitleCase();
        student.BirthDate = request.BirthDate;
        student.BirthPlace = request.BirthPlace;
        student.Gender = request.Gender;
        student.ClassroomId = request.ClassroomId;
        student.PhotoUrl = request.PhotoUrl;
        student.GuardianName = request.GuardianName.ToTitleCase();
        student.GuardianPhone = request.GuardianPhone;
        student.GuardianEmail = request.GuardianEmail;
        student.Address = request.Address;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == student.Id)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateStudentResult(student.Id, newRowVersion);
    }
}
