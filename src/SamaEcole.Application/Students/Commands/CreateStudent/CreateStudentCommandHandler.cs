using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

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
                FullName = request.FullName,
                BirthDate = request.BirthDate,
                Gender = request.Gender,
                ClassroomId = request.ClassroomId,
                GuardianName = request.GuardianName,
                GuardianPhone = request.GuardianPhone
            };

            dbContext.Students.Add(student);
            await dbContext.SaveChangesAsync(ct);

            return new CreateStudentResult(student.Id, student.Matricule);
        }, cancellationToken);
    }
}
