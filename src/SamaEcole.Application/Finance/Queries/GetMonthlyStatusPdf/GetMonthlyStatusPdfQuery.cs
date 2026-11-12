using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Queries.GetMonthlyStatus;

namespace SamaEcole.Application.Finance.Queries.GetMonthlyStatusPdf;

/// <summary>
/// GET /finance/monthly-status/pdf — le MÊME calcul que GetMonthlyStatusQuery (réutilisé, jamais recalculé à part),
/// donc le PDF correspond exactement à l'écran. <see cref="Status"/> (« paid » / « partial » / « unpaid »)
/// restreint le document à une seule catégorie ; null = les trois.
/// </summary>
public record GetMonthlyStatusPdfQuery(Guid SchoolYearId, int Month, Guid? ClassroomId = null, string? Status = null)
    : IRequest<MonthlyStatusPdfFile>;

public record MonthlyStatusPdfFile(byte[] Content, string FileName);

public static class MonthlyStatusPdfStatuses
{
    public const string Paid = "paid";
    public const string Partial = "partial";
    public const string Unpaid = "unpaid";

    public static bool IsValid(string? status) =>
        status is null or Paid or Partial or Unpaid;
}

public sealed class GetMonthlyStatusPdfQueryValidator : AbstractValidator<GetMonthlyStatusPdfQuery>
{
    public GetMonthlyStatusPdfQueryValidator()
    {
        RuleFor(x => x.SchoolYearId).NotEmpty();
        RuleFor(x => x.Month).InclusiveBetween(1, 12);
        // Liste blanche : jamais une valeur libre du client.
        RuleFor(x => x.Status).Must(MonthlyStatusPdfStatuses.IsValid)
            .WithMessage("Catégorie inconnue : choisissez payé, partiellement payé ou non payé.");
    }
}

public class GetMonthlyStatusPdfQueryHandler(
    ISender mediator,
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IMonthlyStatusPdfGenerator pdfGenerator)
    : IRequestHandler<GetMonthlyStatusPdfQuery, MonthlyStatusPdfFile>
{
    public async Task<MonthlyStatusPdfFile> Handle(
        GetMonthlyStatusPdfQuery request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var report = await mediator.Send(
            new GetMonthlyStatusQuery(request.SchoolYearId, request.Month, request.ClassroomId), cancellationToken);

        // `schools` n'est pas une table tenant : le filtre SchoolId est explicite.
        var schoolName = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == schoolId)
            .Select(s => s.Name)
            .SingleAsync(cancellationToken);

        string? classroomName = null;
        if (request.ClassroomId is { } classroomId)
        {
            classroomName = await dbContext.Classrooms.AsNoTracking()
                .Where(c => c.Id == classroomId)
                .Select(c => c.Name)
                .FirstOrDefaultAsync(cancellationToken);
        }

        var content = pdfGenerator.Generate(report, schoolName, classroomName, request.Status);
        var suffix = request.Status is null ? "" : $"_{request.Status}";
        var fileName = $"mensualites_{report.SchoolYearLabel.Replace('/', '-')}_{request.Month:00}{suffix}.pdf";

        return new MonthlyStatusPdfFile(content, fileName);
    }
}
