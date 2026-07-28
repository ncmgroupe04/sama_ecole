namespace SamaEcole.Domain.Enums;

/// <summary>
/// Nombre de segments facturés pour un message. Un SMS n'est pas « une unité » : au-delà d'une
/// longueur donnée, l'opérateur le découpe et facture chaque morceau.
///
/// Vit dans le Domaine parce que DEUX couches en dépendent et doivent impérativement s'accorder :
/// Application (SmsDispatcher débite le solde AVANT l'envoi) et Infrastructure (HttpSmsService
/// rapporte le coût réel APRÈS). Si chacune comptait de son côté, le débit et le rapport
/// divergeraient et le solde dériverait silencieusement — même raison d'être que
/// CycleTypeExtensions.UsesSimplifiedGrading.
///
/// APPROXIMATION ASSUMÉE : on distingue seulement l'alphabet GSM-7 (160 caractères, 153 en
/// multipart) de l'UCS-2 (70 / 67), sans traiter les caractères GSM « étendus » qui comptent double.
/// L'écart possible est d'un segment sur un message déjà long ; l'agrégateur reste la source de
/// vérité de la facturation, ce compteur ne sert qu'à tenir le solde local honnête.
/// </summary>
public static class SmsSegments
{
    private const int Gsm7SingleLimit = 160;
    private const int Gsm7MultipartLimit = 153;
    private const int UnicodeSingleLimit = 70;
    private const int UnicodeMultipartLimit = 67;

    /// <summary>Caractères courants du français hors ASCII qui appartiennent tout de même à l'alphabet GSM-7.</summary>
    private const string Gsm7NonAsciiExtras = "éèùàçÉÈÙÀÇ£¥§¤ÄÖÑÜäöñüß";

    public static int Count(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return 1;
        }

        var isGsm7 = body.All(c => c < 128 || Gsm7NonAsciiExtras.Contains(c));

        var singleLimit = isGsm7 ? Gsm7SingleLimit : UnicodeSingleLimit;
        var multipartLimit = isGsm7 ? Gsm7MultipartLimit : UnicodeMultipartLimit;

        return body.Length <= singleLimit
            ? 1
            : (int)Math.Ceiling(body.Length / (double)multipartLimit);
    }
}
