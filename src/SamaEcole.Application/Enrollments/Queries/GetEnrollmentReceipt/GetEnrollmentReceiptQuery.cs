using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Enrollments.Queries.GetEnrollmentReceipt;

/// <summary>
/// GET /api/v1/enrollments/{id}/receipt — ticket JGK-E01. Relit un reçu déjà émis (réimpression). Le
/// détail des frais vient des lignes FIGÉES à l'inscription, pas du barème courant : le reçu reste
/// donc identique même si le Directeur ajuste ses tarifs par la suite.
///
/// Lecture ouverte à tout utilisateur de l'école (le service Finance encaisse sur la base de ce
/// reçu) : le tenant vient du JWT (RLS + Global Query Filter), un reçu d'une autre école est
/// introuvable ici.
/// </summary>
public record GetEnrollmentReceiptQuery(Guid EnrollmentId) : IRequest<EnrollmentReceiptDto>;

public class GetEnrollmentReceiptQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetEnrollmentReceiptQuery, EnrollmentReceiptDto>
{
    public async Task<EnrollmentReceiptDto> Handle(GetEnrollmentReceiptQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Jointures vers les tables voisines, toutes filtrées sur le même tenant : aucune fuite
        // possible d'un élève, d'une classe ou d'une année d'une autre école.
        var header = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.AsNoTracking() on e.SchoolYearId equals y.Id
            where e.Id == request.EnrollmentId
            select new
            {
                e.Id,
                e.ReceiptNumber,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                YearLabel = y.Label,
                e.Type,
                e.Status,
                e.EnrolledAt,
                e.TotalDue
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        var lines = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == request.EnrollmentId)
            .OrderBy(l => l.IsRecurring).ThenBy(l => l.Designation)
            .Select(l => new EnrollmentFeeLineDto(
                l.Designation, l.IsRecurring, l.UnitAmount, l.Months, l.LineTotal))
            .ToListAsync(cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == schoolId, cancellationToken);

        return new EnrollmentReceiptDto(
            header.Id,
            header.ReceiptNumber,
            school?.Name ?? string.Empty,
            school?.Phone,
            ReceiptCity.FromAddress(school?.Address),
            school?.LogoUrl,
            header.Matricule,
            header.FullName,
            header.ClassroomName,
            header.ClassroomLevel,
            header.YearLabel,
            header.Type.ToString(),
            header.Status.ToString(),
            header.EnrolledAt,
            lines,
            header.TotalDue);
    }
}
