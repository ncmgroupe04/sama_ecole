using SamaEcole.Domain.Entities;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.UpdateGradingScale;

public class UpdateGradingScaleCommandValidator : AbstractValidator<UpdateGradingScaleCommand>
{
    public UpdateGradingScaleCommandValidator()
    {
        RuleFor(c => c.GradingScale)
            .Must(scale => SchoolSettingsDefaults.AllowedGradingScales.Contains(ParseScale(scale)))
            .WithMessage("Le barème doit valoir « 10 » ou « 20 ».");
    }

    private static int ParseScale(string? scale) =>
        int.TryParse(scale, out var value) ? value : -1;
}
