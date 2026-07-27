namespace SamaEcole.Infrastructure.Notifications;

/// <summary>
/// Garde de démarrage du canal SMS — même esprit qu'<see cref="EmailSenderGuard"/>, avec une
/// différence délibérée : l'absence de configuration SMS n'EMPÊCHE PAS le démarrage.
///
/// Un SMTP manquant ferait fuir des mots de passe provisoires dans les journaux, d'où un échec
/// bruyant. Un agrégateur SMS manquant n'expose rien : les SMS sont une option Premium, et la
/// plupart des écoles n'y souscrivent pas. Refuser de démarrer priverait alors toutes les autres
/// d'une application qui fonctionne parfaitement sans.
///
/// Le risque à couvrir est l'inverse : retomber SILENCIEUSEMENT sur LoggingSmsService en production
/// laisserait une école Premium croire que ses parents sont alertés alors que rien ne part. D'où cet
/// avertissement explicite au démarrage.
/// </summary>
public static class SmsServiceGuard
{
    public static string? DescribeMisconfiguration(SmsOptions options, bool isDevelopment)
    {
        if (isDevelopment || options.IsConfigured)
        {
            return null;
        }

        return "Aucun agrégateur SMS configuré (section 'Sms' — voir .env.example) en dehors de "
            + "Development : les envois seront JOURNALISÉS et non transmis. Toute école souscrivant "
            + "l'offre Premium croira ses parents alertés sans qu'aucun SMS ne parte.";
    }
}
