using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetStudentBalance;

/// <summary>
/// GET /finance/students/{studentId}/balance — ticket JGK-F02. Point d'entrée de la caisse : à partir
/// d'un élève trouvé par recherche, retrouve SON INSCRIPTION sur l'année scolaire ACTIVE et le solde à
/// encaisser. Aucune route n'indexait jusqu'ici les inscriptions par élève — tout se faisait par
/// EnrollmentId (reçu). Sans année active ou sans inscription de l'élève sur cette année, il n'y a rien
/// à encaisser : 404, jamais un solde inventé.
///
/// Lecture ouverte à tout rôle de l'école (comme le barème et le reçu) : composer/afficher un solde
/// n'est pas un acte d'encaissement, seul POST /finance/payments l'est (règle #4).
/// </summary>
public record GetStudentBalanceQuery(Guid StudentId) : IRequest<StudentBalanceDto>;

public record StudentBalanceDto(
    Guid EnrollmentId,
    Guid StudentId,
    string Matricule,
    string StudentFullName,
    string ClassroomName,
    string SchoolYearLabel,
    decimal TotalDue,
    decimal AmountPaid,
    decimal RemainingBalance,
    string Status);

public class GetStudentBalanceQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetStudentBalanceQuery, StudentBalanceDto>
{
    public async Task<StudentBalanceDto> Handle(GetStudentBalanceQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà à l'école courante : un élève ou une inscription
        // d'une autre école est structurellement invisible ici, jamais un solde d'un autre tenant.
        var row = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where s.Id == request.StudentId && y.IsActive && e.Status != EnrollmentStatus.Cancelled
            select new StudentBalanceDto(
                e.Id,
                s.Id,
                s.Matricule,
                s.FullName,
                c.Name,
                y.Label,
                e.TotalDue,
                e.AmountPaid,
                e.TotalDue - e.AmountPaid,
                e.AmountPaid >= e.TotalDue ? "Paid" : "Partial")
            ).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Aucune inscription active pour l'élève {request.StudentId} sur l'année scolaire en cours.");

        return row;
    }
}
