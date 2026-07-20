namespace SamaEcole.Application.Common;

/// <summary>
/// Feature B — résout la valeur affichable d'une photo (Student/Teacher) à la LECTURE : une photo
/// téléversée (PhotoData) l'emporte sur une URL externe (PhotoUrl) si les deux sont renseignées — voir
/// le commentaire de <see cref="Domain.Entities.Student.PhotoData"/>.
///
/// Renvoie une URI <c>data:</c> plutôt qu'un lien vers un endpoint binaire dédié : l'API n'utilise que
/// des jetons Bearer (pas de cookie), qu'une balise &lt;img src&gt; ne peut pas porter — embarquer les
/// octets en base64 directement dans le JSON déjà authentifié évite ce problème sans endpoint
/// supplémentaire. Toujours du JPEG (voir PhotoData), donc un content-type fixe.
/// </summary>
public static class PhotoDisplay
{
    public static string? ToDisplayUrl(byte[]? photoData, string? photoUrl) =>
        photoData is { Length: > 0 }
            ? $"data:image/jpeg;base64,{Convert.ToBase64String(photoData)}"
            : photoUrl;
}
