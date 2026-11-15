using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Pages juridiques PUBLIQUES (anonymes, indexables) : conditions d'utilisation, politique de confidentialité,
/// mentions légales, cookies et stockage local. Gabarits Razor sans appel d'API ; l'identité de l'éditeur vient de la
/// configuration (<see cref="Security.LegalOptions"/>) et la version en vigueur de <c>LegalDocuments.CurrentVersion</c>.
/// </summary>
[AllowAnonymous]
public class LegalController : Controller
{
    [HttpGet("/conditions-utilisation")]
    public IActionResult Terms() => View("~/Views/Legal/Terms.cshtml");

    [HttpGet("/politique-confidentialite")]
    public IActionResult Privacy() => View("~/Views/Legal/Privacy.cshtml");

    [HttpGet("/mentions-legales")]
    public IActionResult Notice() => View("~/Views/Legal/Notice.cshtml");

    [HttpGet("/cookies")]
    public IActionResult Cookies() => View("~/Views/Legal/Cookies.cshtml");
}
