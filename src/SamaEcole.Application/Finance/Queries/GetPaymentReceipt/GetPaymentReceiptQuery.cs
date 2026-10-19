using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Application.Finance;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPaymentReceipt;

/// <summary>
/// GET /finance/payments/{id}/receipt — ticket JGK-F02. Relit un reçu de paiement déjà émis (pour la
/// réimpression comme pour le rendu PDF). Les montants viennent de la ligne de paiement FIGÉE
/// (Amount, BalanceAfter) : le reçu reste identique même si l'élève verse à nouveau ensuite.
///
/// Lecture ouverte à tout utilisateur de l'école : le tenant vient du JWT (RLS + Global Query Filter),
/// un paiement d'une autre école est introuvable ici (404), jamais servi.
/// </summary>
public record GetPaymentReceiptQuery(Guid PaymentId) : IRequest<PaymentReceiptDto>;

public class GetPaymentReceiptQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<GetPaymentReceiptQuery, PaymentReceiptDto>
{
    public async Task<PaymentReceiptDto> Handle(GetPaymentReceiptQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le paiement est lu sous le filtre de tenant (filtre global + RLS) : celui d'une autre école est introuvable.
        // L'inscription, l'élève, la classe et l'année peuvent avoir été ARCHIVÉS depuis (élève supprimé après
        // l'annulation de ses paiements) : le reçu, pièce comptable, reste lisible — ils sont donc lus sans filtre
        // d'archivage, avec le SchoolId rétabli à la main (IgnoreQueryFilters retire aussi le filtre de tenant).
        var row = await (
            from p in dbContext.Payments.AsNoTracking()
            join e in dbContext.Enrollments.IgnoreQueryFilters().AsNoTracking() on p.EnrollmentId equals e.Id
            join s in dbContext.Students.IgnoreQueryFilters().AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.IgnoreQueryFilters().AsNoTracking() on e.ClassroomId equals c.Id
            join y in dbContext.SchoolYears.IgnoreQueryFilters().AsNoTracking() on e.SchoolYearId equals y.Id
            where p.Id == request.PaymentId
                  && e.SchoolId == schoolId && s.SchoolId == schoolId && c.SchoolId == schoolId && y.SchoolId == schoolId
            select new
            {
                p.ReceiptNumber,
                p.Amount,
                p.BalanceAfter,
                p.Method,
                p.PaidAt,
                p.Status,
                p.CancelledAt,
                p.CancellationReason,
                p.CancellationOutcome,
                p.ReferencePeriod,
                e.TotalDue,
                s.Matricule,
                s.FullName,
                ClassroomName = c.Name,
                c.IsAccelerated,
                YearLabel = y.Label
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Paiement {request.PaymentId} introuvable.");

        // Ventilation du versement. Ordonnée par Id : l'identifiant est un UUID v7, donc ordonnable —
        // les lignes ressortent dans l'ordre où la caisse les a saisies, sans colonne de rang à stocker.
        // Liste vide si la caisse n'a rien ventilé : le reçu retombe alors sur sa ligne unique.
        var lines = await (
            from b in dbContext.PaymentBreakdowns.AsNoTracking()
            join fc in dbContext.FeeCategories.AsNoTracking() on b.FeeCategoryId equals fc.Id
            where b.PaymentId == request.PaymentId
            orderby b.Id
            select new PaymentReceiptLineDto(fc.Name, b.Label, b.AmountAllocated))
            .ToListAsync(cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .FirstOrDefaultAsync(sc => sc.Id == schoolId, cancellationToken);

        return new PaymentReceiptDto(
            row.ReceiptNumber,
            school?.Name ?? string.Empty,
            school?.Address,
            school?.Email,
            school?.Phone,
            ReceiptCity.FromAddress(school?.Address),
            school?.Ninea,
            school?.RegistreCommerce,
            school?.LogoUrl,
            row.Matricule,
            row.FullName,
            row.ClassroomName,
            row.YearLabel,
            row.Method.ToString(),
            row.Amount,
            row.TotalDue,
            row.TotalDue - row.BalanceAfter,
            row.BalanceAfter,
            row.PaidAt,
            lines,
            row.ReferencePeriod,
            row.IsAccelerated,
            row.Status == PaymentStatus.Cancelled
                ? "REÇU ANNULÉ — " + PaymentCancellationOutcomeLabels.CancelledNote(
                    row.CancelledAt, row.CancellationReason, row.CancellationOutcome)
                : null);
    }
}
