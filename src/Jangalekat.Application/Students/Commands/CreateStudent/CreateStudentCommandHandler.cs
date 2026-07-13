using Jangalekat.Application.Common.Exceptions;
using Jangalekat.Application.Common.Interfaces;
using Jangalekat.Domain.Entities;
using MediatR;

namespace Jangalekat.Application.Students.Commands.CreateStudent;

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

        // Le matricule est calculé ICI, juste avant l'insertion, jamais avant (AGENTS.md règle #3).
        var matricule = await matriculeGenerator.GenerateNextStudentMatriculeAsync(schoolId, cancellationToken);

        var student = new Student
        {
            SchoolId = schoolId,
            Matricule = matricule,
            FullName = request.FullName,
            BirthDate = request.BirthDate,
            Gender = request.Gender,
            ClassroomId = request.ClassroomId,
            GuardianName = request.GuardianName,
            GuardianPhone = request.GuardianPhone
        };

        dbContext.Students.Add(student);

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (IsUniqueConstraintViolation(ex))
        {
            // Deux transactions concurrentes ont tenté le même matricule : à traduire en 409
            // par le pipeline d'exceptions de Jangalekat.Web, jamais en écrasement silencieux.
            throw new ConcurrencyConflictException(nameof(Student), matricule);
        }

        return new CreateStudentResult(student.Id, student.Matricule);
    }

    // Implémentation réelle à brancher sur le type d'exception Npgsql (Jangalekat.Persistence) — placeholder ici
    // pour ne pas faire fuiter une dépendance EF Core/Npgsql dans Application.
    private static bool IsUniqueConstraintViolation(Exception ex) => false;
}
