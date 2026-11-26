namespace SamaEcole.Web.Security;

/// <summary>
/// Identité de l'ÉDITEUR affichée dans les mentions légales, les conditions d'utilisation et la politique de
/// confidentialité (section « Legal » de la configuration). Rien n'est inventé : tant qu'une information n'est pas
/// renseignée, la page l'affiche comme « à compléter » et un bandeau le signale — jamais une valeur fictive.
/// À poser en variables d'environnement <c>Legal__PublisherName</c>, <c>Legal__RegisteredOffice</c>… (voir .env.example).
/// </summary>
public sealed class LegalOptions
{
    public const string SectionName = "Legal";

    /// <summary>Dénomination sociale de l'éditeur de la plateforme.</summary>
    public string? PublisherName { get; set; }

    /// <summary>Forme juridique (SARL, SUARL, entreprise individuelle…).</summary>
    public string? LegalForm { get; set; }

    /// <summary>Siège social / adresse postale.</summary>
    public string? RegisteredOffice { get; set; }

    /// <summary>Adresse e-mail de contact générale ET pour l'exercice des droits sur les données personnelles.</summary>
    public string? ContactEmail { get; set; }

    /// <summary>Téléphone ; repli : le numéro de la vitrine (<c>MarketingCatalog.PhoneDisplay</c>).</summary>
    public string? ContactPhone { get; set; }

    /// <summary>NINEA de l'éditeur.</summary>
    public string? Ninea { get; set; }

    /// <summary>Registre du commerce et du crédit mobilier (RCCM).</summary>
    public string? Rccm { get; set; }

    /// <summary>Directeur de la publication (nom).</summary>
    public string? PublicationDirector { get; set; }

    /// <summary>Les trois informations sans lesquelles les mentions légales ne seraient pas publiables.</summary>
    public bool IsPublisherComplete =>
        !string.IsNullOrWhiteSpace(PublisherName)
        && !string.IsNullOrWhiteSpace(RegisteredOffice)
        && !string.IsNullOrWhiteSpace(ContactEmail);
}
