using FluentValidation;

namespace SamaEcole.Application.Common.Validation;

/// <summary>
/// Feature B — validation de la photo téléversée, partagée par Student et Teacher. Le client compresse
/// déjà l'image (Canvas 300×300, JPEG qualité 80 % — ~30 Ko) AVANT l'envoi : cette borne n'est pas le
/// mécanisme de compression, c'est une défense contre un client modifié ou un appel API direct qui
/// enverrait une image non compressée.
/// </summary>
public static class PhotoValidation
{
    /// <summary>
    /// 500 Ko en octets décodés — grande marge au-dessus des ~30 Ko attendus après compression cliente,
    /// pour ne jamais rejeter une photo légitime, tout en bornant ce qu'un client modifié peut pousser.
    /// </summary>
    public const int MaxPhotoBytes = 500 * 1024;

    /// <summary>Vrai si absent (photo optionnelle) ou si c'est du base64 valide dans la limite de taille.</summary>
    public static bool BeValidPhotoData(string? base64)
    {
        if (string.IsNullOrEmpty(base64))
        {
            return true;
        }

        try
        {
            var bytes = Convert.FromBase64String(base64);
            return bytes.Length > 0 && bytes.Length <= MaxPhotoBytes;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public static IRuleBuilderOptions<T, string?> MustBeValidPhotoData<T>(this IRuleBuilder<T, string?> ruleBuilder) =>
        ruleBuilder.Must(BeValidPhotoData)
            .WithMessage($"La photo doit être une image valide (base64), de {MaxPhotoBytes / 1024} Ko maximum une fois décodée.");
}
