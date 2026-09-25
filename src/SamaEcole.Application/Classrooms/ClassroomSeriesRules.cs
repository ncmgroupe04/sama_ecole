using System.Linq.Expressions;
using SamaEcole.Application.Coefficients;
using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Classrooms;

/// <summary>
/// Règle de saisie de la série d'une classe (Évolution N°4, arbitrages A1/A2), PARTAGÉE par la création et
/// la correction — même principe que <see cref="AcceleratedClassroomRules"/> : une seule définition.
///
/// N'intervient QUE si une série est renseignée : une classe sans série (Seconde commune, primaire,
/// collège…) n'a aucune contrainte de plus. La série n'est possible qu'au LYCÉE et dans le catalogue
/// fermé <see cref="LyceeSeries"/> — l'API reste ouverte (règle #9), c'est ce qui empêche un appel direct
/// d'enregistrer « S9 » ou une série sur une classe de CM2.
/// </summary>
public static class ClassroomSeriesRules
{
    public static void MustDeclareAValidSeries<T>(
        this AbstractValidator<T> validator,
        Expression<Func<T, string?>> series,
        Func<T, string> level)
    {
        var read = series.Compile();

        validator.RuleFor(series)
            // Un seul reproche à la fois : une série sur une classe de primaire est d'abord un problème
            // de cycle, pas de catalogue.
            .Cascade(CascadeMode.Stop)
            .Must((command, _) => ClassroomCycle.CycleFor(level(command)) == CycleType.Lycee)
                .WithMessage("La série n'est possible que pour une classe de Lycée.")
            .Must(value => LyceeSeries.IsValid(LyceeSeries.Normalize(value)))
                .WithMessage(LyceeSeries.UnknownMessage)
            .When(command => LyceeSeries.Normalize(read(command)) is not null);
    }
}
