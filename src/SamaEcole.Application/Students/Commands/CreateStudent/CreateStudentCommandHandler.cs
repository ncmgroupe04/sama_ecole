using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.CreateStudent;

public class CreateStudentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMatriculeGenerator matriculeGenerator)
    : IRequestHandler<CreateStudentCommand, CreateStudentResult>
{
    public async Task<CreateStudentResult> Handle(CreateStudentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // La classe doit exister DANS CETTE ÉCOLE. Le Global Query Filter restreint déjà la requête
        // au tenant courant : une classe d'une autre école est donc introuvable ici, et le contrôle
        // vaut vérification d'appartenance autant que d'existence.
        //
        // Sans ce contrôle, la clé étrangère composite (SchoolId, ClassroomId) rejetterait bien la
        // ligne — mais sous la forme d'une DbUpdateException remontée en 500, là où l'utilisateur
        // mérite une erreur de saisie exploitable sur le bon champ.
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

        // Génération du matricule ET insertion dans une seule transaction (AGENTS.md règle #3) :
        // si l'insertion échoue, le compteur de matricules est rembobiné avec elle — aucun trou.
        // Une violation d'unicité éventuelle est traduite en ConcurrencyConflictException (409)
        // par ApplicationDbContext.SaveChangesAsync, jamais en écrasement silencieux (règle #5).
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var matricule = await matriculeGenerator.GenerateNextStudentMatriculeAsync(schoolId, ct);

            var student = new Student
            {
                SchoolId = schoolId,
                Matricule = matricule,
                FullName = request.FullName.ToTitleCase(),
                BirthDate = request.BirthDate,
                BirthPlace = request.BirthPlace,
                Gender = request.Gender,
                ClassroomId = request.ClassroomId,
                PhotoUrl = request.PhotoUrl,
                PhotoData = request.PhotoData is null ? null : Convert.FromBase64String(request.PhotoData),
                GuardianName = request.GuardianName.ToTitleCase(),
                GuardianPhone = request.GuardianPhone
            };

            dbContext.Students.Add(student);
            await dbContext.SaveChangesAsync(ct);

            return new CreateStudentResult(student.Id, student.Matricule);
        }, cancellationToken);
    }
}
