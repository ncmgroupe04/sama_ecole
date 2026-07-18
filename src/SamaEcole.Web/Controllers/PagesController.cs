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

    // JGK-D03/D04 : liste des enseignants, création de fiche, fiche détaillée avec matières/affectations.
    // Gabarit [AllowAnonymous] côté vue — c'est TeachersController qui garde l'accès (Voir : Super
    // Admin/Directeur/Secrétariat ; Créer/Attribuer : Directeur/Secrétariat) et la RLS qui isole.
    [HttpGet("/enseignants")]
    public IActionResult Teachers() => View("~/Views/Teachers/Index.cshtml");

    // JGK-D06 : écran d'appel — roster d'une classe pour une date/matière/créneau, saisie des statuts.
    // C'est AttendanceController qui garde l'accès (saisie : Enseignant borné à ses classes, Directeur,
    // Secrétariat) et la RLS qui isole.
    [HttpGet("/presences")]
    public IActionResult Attendance() => View("~/Views/Attendance/Index.cshtml");

    [HttpGet("/matieres")]
    public IActionResult Subjects() => View("~/Views/Subjects/Index.cshtml");

    // JGK-G01/G02 : écran de saisie des notes (Devoir/Composition) par classe, matière et trimestre.
    [HttpGet("/notes")]
    public IActionResult Grades() => View("~/Views/Grades/Index.cshtml");

    [HttpGet("/frais")]
    public IActionResult Fees() => View("~/Views/Fees/Index.cshtml");

    [HttpGet("/caisse")]
    public IActionResult Caisse() => View("~/Views/Caisse/Index.cshtml");

    [HttpGet("/parametres")]
    public IActionResult Settings() => View("~/Views/Settings/Index.cshtml");

    // JGK-R02 : rapport d'assiduité détaillé par classe et par élève. Comme les autres pages, gabarit
    // [AllowAnonymous] côté vue (le JWT ne voyage pas en navigation) — c'est ReportsController qui garde
    // l'accès (Directeur/Secrétariat/Super Admin) et la RLS qui isole.
    [HttpGet("/rapports/assiduite")]
    public IActionResult AttendanceReport() => View("~/Views/Reports/Attendance.cshtml");

    // JGK-I03 : espace Super Admin de revue des demandes d'inscription self-service. Comme les autres
    // pages, [AllowAnonymous] côté vue (le JWT ne voyage pas en navigation) — c'est
    // AdminRegistrationRequestsController qui garde l'accès (Roles = SuperAdmin) et la RLS qui isole.
    [HttpGet("/admin/inscriptions")]
    public IActionResult RegistrationRequests() => View("~/Views/Admin/RegistrationRequests.cshtml");

    // Les années scolaires ne sont plus une entrée de menu à part : elles vivent désormais dans les
    // Paramètres (onglet dédié). On redirige l'ancienne adresse pour ne casser aucun lien existant.
    [HttpGet("/annees-scolaires")]
    public IActionResult SchoolYears() => RedirectToAction(nameof(Settings), new { tab = "annees-scolaires" });
}
