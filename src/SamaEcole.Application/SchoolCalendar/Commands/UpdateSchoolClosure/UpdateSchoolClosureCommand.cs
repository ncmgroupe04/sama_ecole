using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.SchoolCalendar.Commands.UpdateSchoolClosure;

/// <summary>
/// PUT /api/v1/school-calendar/closures/{id} — corrige les dates, la nature ou les libellés. L'année
/// d'une fermeture ne change jamais (la supprimer puis la recréer sur l'autre année). L'id vient de la
/// ROUTE et écrase celui du corps.
/// </summary>
public record UpdateSchoolClosureCommand : IRequest<SchoolClosureDto>
{
    public Guid Id { get; init; }
    public required DateOnly StartDate { get; init; }
    public required DateOnly EndDate { get; init; }
    public SchoolClosureKind Kind { get; init; }
    public required string Label { get; init; }
    public string? LabelAr { get; init; }
}

public class UpdateSchoolClosureCommandValidator : AbstractValidator<UpdateSchoolClosureCommand>
{
    public UpdateSchoolClosureCommandValidator()
    {
        RuleFor(c => c.EndDate).GreaterThanOrEqualTo(c => c.StartDate)
            .WithMessage("La date de fin ne peut pas précéder la date de début.");
        RuleFor(c => c.Kind).IsInEnum();
        RuleFor(c => c.Label).ValidLabel();
        RuleFor(c => c.LabelAr).ValidLabelAr();
    }
}

public class UpdateSchoolClosureCommandHandler(
    IApplicationDbContext dbContext,
    ILogger<UpdateSchoolClosureCommandHandler> logger)
    : IRequestHandler<UpdateSchoolClosureCommand, SchoolClosureDto>
{
    public async Task<SchoolClosureDto> Handle(UpdateSchoolClosureCommand request, CancellationToken cancellationToken)
    {
        // Global Query Filter + RLS : la fermeture d'une autre école est introuvable (404).
        var closure = await dbContext.SchoolClosures.FirstOrDefaultAsync(c => c.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Fermeture {request.Id} introuvable.");

        await SchoolClosureRules.EnsureWithinYearAsync(
            dbContext, closure.SchoolYearId, request.StartDate, request.EndDate,
            nameof(request.StartDate), cancellationToken);

        closure.StartDate = request.StartDate;
        closure.EndDate = request.EndDate;
        closure.Kind = request.Kind;
        closure.Label = request.Label.Trim();
        closure.LabelAr = string.IsNullOrWhiteSpace(request.LabelAr) ? null : request.LabelAr.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Fermeture {ClosureId} modifiée.", closure.Id);

        return new SchoolClosureDto(
            closure.Id, closure.SchoolYearId, closure.StartDate, closure.EndDate,
            closure.Kind, closure.Label, closure.LabelAr);
    }
}
