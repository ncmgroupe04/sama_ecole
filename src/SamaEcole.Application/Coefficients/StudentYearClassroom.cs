using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Coefficients;

/// <summary>
/// Classe de référence d'un élève pour UNE année (arbitrage A11) : celle de son INSCRIPTION de l'année — un
/// élève passé de S2 à L2 garde, pour l'année précédente, la configuration de S2 — et, à défaut d'inscription,
/// sa classe actuelle. Une seule définition, partagée par les coefficients (CoefficientOverrideLoader) et les
/// matières suivies (SubjectFollowScope) : un bulletin ne peut pas lire deux classes différentes.
/// </summary>
public static class StudentYearClassroom
{
    public static async Task<Guid?> ResolveAsync(
        IApplicationDbContext dbContext, Guid studentId, Guid schoolYearId, CancellationToken cancellationToken)
        => await dbContext.Enrollments.AsNoTracking()
               .Where(e => e.StudentId == studentId
                           && e.SchoolYearId == schoolYearId
                           && e.Status != EnrollmentStatus.Cancelled)
               .Select(e => (Guid?)e.ClassroomId)
               .FirstOrDefaultAsync(cancellationToken)
           ?? await dbContext.Students.AsNoTracking()
               .Where(s => s.Id == studentId)
               .Select(s => (Guid?)s.ClassroomId)
               .FirstOrDefaultAsync(cancellationToken);
}
