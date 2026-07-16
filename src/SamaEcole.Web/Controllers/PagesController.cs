using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Sert les vues Razor de l'application. Aucune ne portait de contrôleur jusqu'ici : elles étaient
/// donc inaccessibles (la route par défaut pointe vers un Home/Index qui n'existe pas).
///
/// Ces pages sont volontairement [AllowAnonymous] : l'access token vit dans localStorage, et un
/// navigateur n'envoie pas d'en-tête Authorization en naviguant — un [Authorize] ici renverrait 401
/// à tout le monde, y compris aux utilisateurs connectés. La page est donc un gabarit vide, et ce
/// sont les APPELS D'API qu'elle déclenche qui sont protégés (StudentsController est [Authorize] et
/// la RLS PostgreSQL isole le tenant). Le garde-fou de wwwroot/js/auth.js qui redirige vers /login
/// n'est qu'un confort d'affichage : il n'expose aucune donnée s'il est contourné.
/// </summary>
[AllowAnonymous]
public class PagesController : Controller
{
    [HttpGet("/")]
    public IActionResult Index() => RedirectToAction(nameof(Students));

    // JGK-F04. Route dédiée plutôt que remplacer l'atterrissage par défaut (/eleves) : c'est un
    // tableau de bord FINANCIER, pas un accueil générique — seuls Directeur et Finance en ont l'usage
    // (voir FinanceController.Dashboard), le Secrétariat continue d'atterrir sur les élèves.
    [HttpGet("/tableau-de-bord")]
    public IActionResult Dashboard() => View("~/Views/Dashboard/Index.cshtml");

    [HttpGet("/eleves")]
    public IActionResult Students() => View("~/Views/Students/Index.cshtml");

    [HttpGet("/inscriptions")]
    public IActionResult Enrollments() => View("~/Views/Enrollments/Index.cshtml");

    [HttpGet("/classes")]
    public IActionResult Classrooms() => View("~/Views/Classrooms/Index.cshtml");

    [HttpGet("/matieres")]
    public IActionResult Subjects() => View("~/Views/Subjects/Index.cshtml");

    [HttpGet("/frais")]
    public IActionResult Fees() => View("~/Views/Fees/Index.cshtml");

    [HttpGet("/caisse")]
    public IActionResult Caisse() => View("~/Views/Caisse/Index.cshtml");

    [HttpGet("/parametres")]
    public IActionResult Settings() => View("~/Views/Settings/Index.cshtml");

    // Les années scolaires ne sont plus une entrée de menu à part : elles vivent désormais dans les
    // Paramètres (onglet dédié). On redirige l'ancienne adresse pour ne casser aucun lien existant.
    [HttpGet("/annees-scolaires")]
    public IActionResult SchoolYears() => RedirectToAction(nameof(Settings), new { tab = "annees-scolaires" });
}
