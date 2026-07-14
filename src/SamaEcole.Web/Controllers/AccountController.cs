using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Sert l'écran de connexion (Views/Account/Login.cshtml). Contrôleur de VUE, pas d'API : il ne
/// vérifie aucun identifiant — la soumission part en fetch vers POST /api/v1/auth/login
/// (wwwroot/js/auth.js). La déconnexion, elle, n'a pas de page : c'est un appel d'API suivi d'une
/// redirection, déclenché depuis la barre supérieure.
/// </summary>
[AllowAnonymous]
public class AccountController : Controller
{
    [HttpGet("/login")]
    public IActionResult Login() => View();
}
