using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Sert les écrans PUBLICS de connexion, d'inscription self-service (ticket JGK-I01) et de suivi de
/// demande (ticket JGK-I02). Contrôleur de VUE, pas d'API : il ne vérifie aucun identifiant — la
/// soumission part en fetch vers POST /api/v1/auth/login (wwwroot/js/auth.js), POST
/// /api/v1/registration-requests (wwwroot/js/registration.js) ou GET
/// /api/v1/registration-requests/{ref}/status (wwwroot/js/tracking.js). La déconnexion, elle, n'a pas
/// de page : c'est un appel d'API suivi d'une redirection, déclenché depuis la barre supérieure.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    [HttpGet("/login")]
    public IActionResult Login() => View();

    [HttpGet("/inscription")]
    public IActionResult Register() => View();

    [HttpGet("/suivi-demande")]
    public IActionResult Track() => View();

    // Ticket JGK-I04 — page d'atterrissage quand SubscriptionAwaitingPaymentMiddleware bloque un appel
    // d'API (wwwroot/js/api.js y redirige sur le code SUBSCRIPTION_AWAITING_PAYMENT). [AllowAnonymous]
    // comme le reste de ce contrôleur : un visiteur non connecté y est renvoyé vers /login par son
    // propre script (voir la vue), le serveur ne peut de toute façon pas connaître son identité ici
    // (le JWT vit en localStorage, jamais sur une navigation classique).
    [HttpGet("/abonnement/paiement")]
    public IActionResult SubscriptionPending() => View();
}
