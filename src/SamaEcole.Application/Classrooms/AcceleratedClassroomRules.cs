using System.Linq.Expressions;
using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Classrooms;

/// <summary>
/// Règles de saisie d'une classe passerelle / accélérée, PARTAGÉES par la création et la correction —
/// une seule définition, sans quoi les deux écrans finiraient par accepter des choses différentes.
///
/// Le second niveau vient d'une liste déroulante à l'écran, mais l'API reste ouverte (AGENTS.md règle #9) :
/// ces règles sont ce qui empêche un appel direct d'enregistrer une passerelle incohérente — un niveau
/// hors nomenclature, ou une passerelle qui « mène » au niveau dont elle part.
/// </summary>
public static class AcceleratedClassroomRules
{
    /// <summary>
    /// N'intervient QUE si la case « classe accélérée » est cochée : une classe ordinaire n'a aucune
    /// contrainte supplémentaire, l'option ne durcit rien pour qui ne s'en sert pas.
    /// </summary>
    public static void MustDeclareACoherentAcceleratedPath<T>(
        this AbstractValidator<T> validator,
        Func<T, bool> isAccelerated,
        Expression<Func<T, string?>> targetLevel,
        Func<T, string> classroomName,
        Func<T, string> level)
    {
        validator.RuleFor(targetLevel)
            // Stop au premier échec : sans cascade, un champ vide déclencherait AUSSI « niveau inconnu »,
            // et l'écran afficherait deux reproches pour une seule case à remplir.
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
                .WithMessage("Sélectionnez le second niveau validé par cette classe passerelle.")
            .MaximumLength(50)
            .NoHtml()
            .Must((command, target) =>
                ClassroomGradeLevels.IsKnown(target, ClassroomCycle.CycleFor(level(command))))
                .WithMessage("Ce second niveau n'appartient pas au cycle de la classe.")
            .Must((command, target) => !IsSameAsCurrentLevel(target, classroomName(command), level(command)))
                .WithMessage("Le second niveau doit être différent du niveau de départ de la classe.")
            .When(command => isAccelerated(command));
    }

    /// <summary>
    /// Une passerelle valide deux niveaux DIFFÉRENTS. Comparaison au niveau reconnu dans le nom de la
    /// classe (« CI-CP » → CI) ; un nom hors nomenclature ne donne rien à comparer et la règle laisse
    /// alors passer, plutôt que de bloquer une école dont la nomenclature nous échappe.
    /// </summary>
    private static bool IsSameAsCurrentLevel(string? targetLevel, string classroomName, string level)
    {
        var currentLevel = ClassroomGradeLevels.FromClassroomName(classroomName, ClassroomCycle.CycleFor(level));

        return currentLevel is not null
               && string.Equals(currentLevel, targetLevel?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
