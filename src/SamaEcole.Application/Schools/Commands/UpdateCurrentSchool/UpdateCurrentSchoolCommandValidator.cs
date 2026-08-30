using SamaEcole.Application.Common.Validation;
using FluentValidation;

namespace SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;

/// <summary>
/// Le nom est la seule donnée obligatoire (il figure en gros sur le reçu, il ne peut pas être vide).
/// Adresse, téléphone et logo sont facultatifs. Le logo, s'il est fourni, doit être une URL http(s) :
/// le reçu ne pointera jamais un <c>file://</c> ou un <c>javascript:</c>.
/// </summary>
public class UpdateCurrentSchoolCommandValidator : AbstractValidator<UpdateCurrentSchoolCommand>
{
    public UpdateCurrentSchoolCommandValidator()
    {
        RuleFor(c => c.Name)
            .NotEmpty().WithMessage("Le nom de l'établissement est obligatoire.")
            .MaximumLength(200).WithMessage("Le nom ne peut pas dépasser 200 caractères.")
            .NoHtml();

        RuleFor(c => c.Address)
            .MaximumLength(300).WithMessage("L'adresse ne peut pas dépasser 300 caractères.")
            .NoHtml();

        RuleFor(c => c.Phone)
            .MaximumLength(30).WithMessage("Le téléphone ne peut pas dépasser 30 caractères.")
            .NoHtml();

        RuleFor(c => c.LogoUrl)
            .MaximumLength(500).WithMessage("L'URL du logo ne peut pas dépasser 500 caractères.")
            .Must(BeAValidHttpUrl).When(c => !string.IsNullOrWhiteSpace(c.LogoUrl))
            .WithMessage("L'URL du logo doit être une adresse http(s) valide.");

        // En-tête administratif du bulletin. Facultatifs : les rendre obligatoires bloquerait toute
        // école qui corrige juste son adresse — la ligne du bulletin s'imprime simplement vide.
        RuleFor(c => c.InspectionAcademie)
            .MaximumLength(150).WithMessage("L'Inspection d'Académie ne peut pas dépasser 150 caractères.")
            .NoHtml();

        RuleFor(c => c.InspectionEducationFormation)
            .MaximumLength(150).WithMessage("L'IEF ne peut pas dépasser 150 caractères.")
            .NoHtml();

        RuleFor(c => c.NomLycee)
            .MaximumLength(150).WithMessage("Le nom d'établissement du bulletin ne peut pas dépasser 150 caractères.")
            .NoHtml();

        // Coordonnées et mentions légales de l'en-tête du reçu. Facultatives : une école qui n'a pas
        // encore son RCCM doit pouvoir travailler — la mention manquante ne s'imprime simplement pas.
        RuleFor(c => c.Email)
            .MaximumLength(150).WithMessage("L'e-mail ne peut pas dépasser 150 caractères.")
            .EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email))
            .WithMessage("L'adresse e-mail n'est pas valide.");

        // Format NON contraint : le NINEA sénégalais a déjà changé de longueur, et le RCCM s'écrit
        // « SN DKR 2020 B 1234 » avec des variantes selon le greffe. Une regex ici bloquerait des
        // établissements parfaitement en règle ; on borne la longueur et on refuse le HTML, rien de plus.
        RuleFor(c => c.Ninea)
            .MaximumLength(50).WithMessage("Le NINEA ne peut pas dépasser 50 caractères.")
            .NoHtml();

        RuleFor(c => c.RegistreCommerce)
            .MaximumLength(50).WithMessage("Le registre du commerce ne peut pas dépasser 50 caractères.")
            .NoHtml();

        // Annuaire public. NoHtml() compte double ici : ces trois champs sont les SEULS de
        // l'application à être servis tels quels à des visiteurs anonymes — une balise passée dans une
        // présentation d'établissement serait rendue sur la vitrine, pour tout le monde.
        RuleFor(c => c.City)
            .MaximumLength(120).WithMessage("La ville ne peut pas dépasser 120 caractères.")
            .NoHtml();

        RuleFor(c => c.Region)
            .MaximumLength(120).WithMessage("La région ne peut pas dépasser 120 caractères.")
            .NoHtml();

        RuleFor(c => c.PublicDescription)
            .MaximumLength(2000).WithMessage("La présentation publique ne peut pas dépasser 2000 caractères.")
            .NoHtml();

        // Une école ne peut pas entrer dans l'annuaire sans ville : c'est le premier critère de
        // recherche d'un parent, et une fiche sans localisation y serait invisible ou trompeuse.
        // Contrainte appliquée SEULEMENT à la publication — une école non listée reste libre de ne rien
        // renseigner.
        RuleFor(c => c.City)
            .NotEmpty().When(c => c.IsPubliclyListed)
            .WithMessage("La ville est obligatoire pour figurer dans l'annuaire public.");

        // Intégration étatique (SIMEN). Formats NON contraints par regex, même raison que le NINEA :
        // les codes du ministère varient de forme selon le document source. On borne la longueur,
        // on refuse le HTML.
        RuleFor(c => c.NationalSchoolCode)
            .MaximumLength(30).WithMessage("Le code établissement national ne peut pas dépasser 30 caractères.")
            .NoHtml();

        RuleFor(c => c.MinistryAuthorizationNumber)
            .MaximumLength(80).WithMessage("Le numéro d'autorisation ne peut pas dépasser 80 caractères.")
            .NoHtml();

        RuleFor(c => c.SchoolDistrictCode)
            .MaximumLength(30).WithMessage("Le code de circonscription ne peut pas dépasser 30 caractères.")
            .NoHtml();

        // Coordonnées GPS : bornes WGS84 strictes. Une valeur hors bornes est une faute de saisie
        // (virgule/point, ordre lat/lon inversé) — la refuser tôt évite un point placé au milieu de
        // l'océan sur la carte scolaire.
        RuleFor(c => c.GpsLatitude)
            .InclusiveBetween(-90m, 90m).When(c => c.GpsLatitude.HasValue)
            .WithMessage("La latitude doit être comprise entre -90 et 90.");

        RuleFor(c => c.GpsLongitude)
            .InclusiveBetween(-180m, 180m).When(c => c.GpsLongitude.HasValue)
            .WithMessage("La longitude doit être comprise entre -180 et 180.");

        // Les deux ensemble ou aucune : une latitude sans longitude (ou l'inverse) ne localise rien.
        RuleFor(c => c)
            .Must(c => c.GpsLatitude.HasValue == c.GpsLongitude.HasValue)
            .WithMessage("Renseignez la latitude ET la longitude, ou laissez les deux vides.");
    }

    private static bool BeAValidHttpUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
