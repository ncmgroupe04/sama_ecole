using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Queries.GetDischargeNotePdf;

public class GetDischargeNotePdfQueryHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IDischargeNotePdfGenerator pdfGenerator,
    ISchoolLogoProvider logoProvider,
    IQrCodeService qrCodeService)
    : IRequestHandler<GetDischargeNotePdfQuery, DischargeNotePdfResult>
{
    public async Task<DischargeNotePdfResult> Handle(
        GetDischargeNotePdfQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : la fiche
        // d'une autre école renvoie 404, jamais un PDF nommant un élève d'un autre établissement.
        var assignment = await dbContext.ItemAssignments.AsNoTracking()
            .Where(a => a.Id == request.AssignmentId)
            .Select(a => new
            {
                a.Id,
                a.ItemId,
                a.Quantity,
                a.BeneficiaryType,
                a.StudentId,
                a.UserId,
                a.BeneficiaryLabel,
                a.AssignedOn,
                a.DueOn,
                a.ReturnedOn,
                a.ReturnedQuantity,
                Status = a.Status.ToString(),
                a.Notes,
                ItemName = dbContext.InventoryItems
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => i.Name)
                    .FirstOrDefault() ?? "Bien archivé",
                ItemCode = dbContext.InventoryItems
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => i.Code)
                    .FirstOrDefault(),
                ItemCondition = dbContext.InventoryItems
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => i.Condition.ToString())
                    .FirstOrDefault() ?? string.Empty,
                CategoryName = dbContext.InventoryItems
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => dbContext.InventoryCategories
                        .Where(c => c.Id == i.CategoryId)
                        .Select(c => c.Name)
                        .FirstOrDefault())
                    .FirstOrDefault() ?? "Sans catégorie"
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Fiche de prêt {request.AssignmentId} introuvable.");

        var beneficiaryDetail = await BeneficiaryDetailAsync(
            assignment.BeneficiaryType, assignment.StudentId, assignment.UserId, cancellationToken);

        var school = await SchoolHeaderAsync(cancellationToken);

        var reference = AssignmentReference.For(assignment.Id, assignment.AssignedOn);

        var model = new DischargeNoteModel(
            reference,
            school.Name,
            school.InspectionAcademie,
            school.InspectionEducationFormation,
            school.Address,
            school.Phone,
            assignment.BeneficiaryType.ToString(),
            assignment.BeneficiaryLabel,
            beneficiaryDetail,
            assignment.ItemName,
            assignment.ItemCode,
            assignment.CategoryName,
            assignment.Quantity,
            assignment.ItemCondition,
            assignment.AssignedOn,
            assignment.DueOn,
            assignment.Status,
            assignment.ReturnedOn,
            assignment.ReturnedQuantity ?? 0,
            assignment.Notes);

        // Best effort, comme partout ailleurs : une URL de logo injoignable ne doit pas empêcher
        // l'émission d'une pièce officielle (voir ISchoolLogoProvider).
        var logo = await logoProvider.TryFetchAsync(school.LogoUrl, cancellationToken);
        var qrCode = qrCodeService.GenerateQrCode($"https://app.samaecole.sn/verify?ref={reference}");

        return new DischargeNotePdfResult(pdfGenerator.Generate(model, logo, qrCode), reference);
    }

    /// <summary>
    /// Classe de l'élève, ou fonction du membre du personnel. Null pour un enseignant : sa « fonction »
    /// est déjà dite par le type de bénéficiaire, et inventer une matière de rattachement sur une
    /// décharge de vidéoprojecteur n'apporterait rien.
    /// </summary>
    private async Task<string?> BeneficiaryDetailAsync(
        AssignmentBeneficiaryType type, Guid? studentId, Guid? userId, CancellationToken cancellationToken) => type switch
    {
        AssignmentBeneficiaryType.Eleve when studentId is { } id => await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == id)
            .Select(s => dbContext.Classrooms.Where(c => c.Id == s.ClassroomId).Select(c => c.Name).FirstOrDefault())
            .FirstOrDefaultAsync(cancellationToken),

        AssignmentBeneficiaryType.Personnel when userId is { } id => await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == id && u.SchoolId == tenantProvider.CurrentSchoolId)
            .Select(u => u.Role.ToString())
            .FirstOrDefaultAsync(cancellationToken),

        _ => null
    };

    private async Task<(string Name, string? InspectionAcademie, string? InspectionEducationFormation,
        string? Address, string? Phone, string? LogoUrl)> SchoolHeaderAsync(CancellationToken cancellationToken)
    {
        if (tenantProvider.CurrentSchoolId is not { } schoolId)
        {
            return (string.Empty, null, null, null, null, null);
        }

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => new
            {
                s.Name,
                s.InspectionAcademie,
                s.InspectionEducationFormation,
                s.Address,
                s.Phone,
                s.LogoUrl
            })
            .FirstOrDefaultAsync(cancellationToken);

        return (school?.Name ?? string.Empty, school?.InspectionAcademie, school?.InspectionEducationFormation,
            school?.Address, school?.Phone, school?.LogoUrl);
    }
}
