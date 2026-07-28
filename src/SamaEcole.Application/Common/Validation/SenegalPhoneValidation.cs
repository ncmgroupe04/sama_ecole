using System.Text.RegularExpressions;
using FluentValidation;

namespace SamaEcole.Application.Common.Validation;

public static class SenegalPhoneValidation
{
    // Numéros du Sénégal: optionnellement précédés de +221 ou 00221, suivis de 77, 76, 78, 70, 75 ou 33, puis 7 chiffres (espaces autorisés).
    private static readonly Regex SenegalPhoneRegex = new(
        @"^(?:\+221|00221)?\s?(?:77|76|78|70|75|33)(?:\s?\d){7}$",
        RegexOptions.Compiled);

    public static IRuleBuilderOptions<T, string?> MustBeValidSenegalPhone<T>(this IRuleBuilder<T, string?> ruleBuilder)
    {
        return ruleBuilder
            .Must(phone => string.IsNullOrWhiteSpace(phone) || SenegalPhoneRegex.IsMatch(phone))
            .WithMessage("Le numéro de téléphone doit être un numéro sénégalais valide (ex: 77 123 45 67).");
    }

    /// <summary>Même règle que <see cref="MustBeValidSenegalPhone{T}"/>, hors contexte FluentValidation
    /// (validation ligne par ligne d'un import de masse — voir ImportTeachersCommandHandler).</summary>
    public static bool IsValidSenegalPhone(string phone) => SenegalPhoneRegex.IsMatch(phone);
}
