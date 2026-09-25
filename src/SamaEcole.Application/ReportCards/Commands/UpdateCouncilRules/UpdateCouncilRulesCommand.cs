using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.ReportCards.Commands.UpdateCouncilRules;

/// <summary>
/// PUT /api/v1/report-cards/council-rules — le Directeur règle les seuils du conseil de classe (Évolution N°7),
/// tous sur /20 : distinctions, note éliminatoire, passage et redoublement. Recalcule les propositions des
/// bulletins et PV (jamais une décision déjà prise par le conseil). Journalisé.
/// </summary>
public record UpdateCouncilRulesCommand(
    decimal FelicitationsMin,
    decimal HonorRollMin,
    decimal EncouragementsMin,
    decimal EliminatoryGrade,
    decimal PromotionMin,
    decimal RepeatMin) : IRequest<CouncilRules>, IAuditableRequest
{
    public CouncilRules ToRules() => new(FelicitationsMin, HonorRollMin, EncouragementsMin, EliminatoryGrade, PromotionMin, RepeatMin);
}

public class UpdateCouncilRulesCommandValidator : AbstractValidator<UpdateCouncilRulesCommand>
{
    public UpdateCouncilRulesCommandValidator()
        => RuleFor(x => x).Custom((command, context) =>
        {
            foreach (var message in command.ToRules().Validate())
            {
                context.AddFailure("CouncilRules", message);
            }
        });
}

public class UpdateCouncilRulesCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpdateCouncilRulesCommand, CouncilRules>
{
    public async Task<CouncilRules> Handle(UpdateCouncilRulesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var settings = await dbContext.SchoolSettings.FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
        if (settings is null)
        {
            settings = new SchoolSettings { SchoolId = schoolId };
            dbContext.SchoolSettings.Add(settings);
        }

        settings.CouncilFelicitationsMin = request.FelicitationsMin;
        settings.CouncilHonorRollMin = request.HonorRollMin;
        settings.CouncilEncouragementsMin = request.EncouragementsMin;
        settings.CouncilEliminatoryGrade = request.EliminatoryGrade;
        settings.CouncilPromotionMin = request.PromotionMin;
        settings.CouncilRepeatMin = request.RepeatMin;

        await dbContext.SaveChangesAsync(cancellationToken);
        return CouncilRules.From(settings);
    }
}
