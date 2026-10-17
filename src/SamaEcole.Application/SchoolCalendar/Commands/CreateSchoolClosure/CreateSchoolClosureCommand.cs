using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.SchoolCalendar.Commands.CreateSchoolClosure;

/// <summary>
/// POST /api/v1/school-calendar/closures — ajoute un jour férié, des vacances ou une autre fermeture
/// à une année scolaire. Le SchoolId vient du JWT (règle #10), jamais du corps.
/// </summary>
public record CreateSchoolClosureCommand(
    Guid SchoolYearId,
    DateOnly StartDate,
    DateOnly EndDate,
    SchoolClosureKind Kind,
    string Label,
    string? LabelAr = null) : IRequest<SchoolClosureDto>;

public class CreateSchoolClosureCommandValidator : AbstractValidator<CreateSchoolClosureCommand>
{
    public CreateSchoolClosureCommandValidator()
    {
        RuleFor(c => c.SchoolYearId).NotEmpty();
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.StartDate)
            .WithMessage("La date de fin ne peut pas précéder la date de début.");
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Label).ValidLabel();
        RuleFor(c => c.LabelAr).ValidLabelAr();
    }
}

public class CreateSchoolClosureCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ILogger<CreateSchoolClosureCommandHandler> logger)
    : IRequestHandler<CreateSchoolClosureCommand, SchoolClosureDto>
{
    public async Task<SchoolClosureDto> Handle(CreateSchoolClosureCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await SchoolClosureRules.EnsureWithinYearAsync(
            dbContext, request.SchoolYearId, request.StartDate, request.EndDate,
            nameof(request.StartDate), cancellationToken);

        var closure = new SchoolClosure
        {
            SchoolId = schoolId,
            SchoolYearId = request.SchoolYearId,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Kind = request.Kind,
            Label = request.Label.Trim(),
            LabelAr = string.IsNullOrWhiteSpace(request.LabelAr) ? null : request.LabelAr.Trim()
        };

        dbContext.SchoolClosures.Add(closure);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Fermeture {ClosureId} « {Label} » ajoutée à l'année {YearId} de l'école {SchoolId} ({Start} – {End}).",
            closure.Id, closure.Label, closure.SchoolYearId, schoolId, closure.StartDate, closure.EndDate);

        return new SchoolClosureDto(
            closure.Id, closure.SchoolYearId, closure.StartDate, closure.EndDate,
            closure.Kind, closure.Label, closure.LabelAr);
    }
}
