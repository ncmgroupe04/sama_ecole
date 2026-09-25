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

    // Module Infrastructures : gestion physique des locaux (Bâtiments/Salles), indépendante des
    // classes pédagogiques. BuildingsController/RoomsController gardent l'accès et la RLS isole.
    [HttpGet("/infrastructures")]
    public IActionResult Buildings() => View("~/Views/Buildings/Index.cshtml");

    // Module Inventaire : patrimoine, journal de stock, prêts de matériel. InventoryController garde
    // l'accès (lecture ouverte à tout rôle authentifié ; catalogue réservé Directeur/Secrétariat ;
    // mouvements et prêts ouverts en plus au Surveillant) et la RLS isole.
    [HttpGet("/inventaire")]
    public IActionResult Inventory() => View("~/Views/Inventory/Index.cshtml");

    // Module Internat : logement, régime, affectation de chambre. InternatController garde l'accès
    // ([RequireModule(SchoolModule.Internat)] + rôles) et la RLS isole.
    [HttpGet("/internat")]
    public IActionResult Internat() => View("~/Views/Internat/Index.cshtml");

    // JGK-D03/D04 : liste des enseignants, création de fiche, fiche détaillée avec matières/affectations.
    // Gabarit [AllowAnonymous] côté vue — c'est TeachersController qui garde l'accès (Voir : Super
    // Admin/Directeur/Secrétariat ; Créer/Attribuer : Directeur/Secrétariat) et la RLS qui isole.
    [HttpGet("/enseignants")]
    public IActionResult Teachers() => View("~/Views/Teachers/Index.cshtml");

    // JGK-D06 : écran d'appel — roster d'une classe pour une date/matière/créneau, saisie des statuts.
    // C'est AttendanceController qui garde l'accès (saisie : Enseignant borné à ses classes, Directeur,
    // Secrétariat, Surveillant) et la RLS qui isole.
    [HttpGet("/presences")]
    public IActionResult Attendance() => View("~/Views/Attendance/Index.cshtml");

    [HttpGet("/billets")]
    public IActionResult Billets() => View("~/Views/Absences/Billets.cshtml");

    [HttpGet("/pointage-profs")]
    public IActionResult PointageProfs() => View("~/Views/Absences/PointageProfs.cshtml");

    [HttpGet("/discipline")]
    public IActionResult Discipline() => View("~/Views/Discipline/Index.cshtml");

    // Convocations de parent/tuteur (module Vie Scolaire) — ParentSummonsController garde l'accès.
    [HttpGet("/convocations")]
    public IActionResult ParentSummons() => View("~/Views/ParentSummons/Index.cshtml");

    // Pas de route /billet-print : le billet de retard et le billet de sortie sont des PDF A5 générés
    // PAR LE SERVEUR (BilletsController, GetEntryTicketQuery), téléchargés depuis /billets par
    // billets.js. Aucune page HTML d'impression n'est donc nécessaire — la vue de remplacement qui
    // occupait cette route n'a jamais eu de contenu.

    [HttpGet("/matieres")]
    public IActionResult Subjects() => View("~/Views/Subjects/Index.cshtml");

    // JGK-G01/G02 : écran de saisie des notes (Devoir/Composition) par classe, matière et trimestre.
    [HttpGet("/notes")]
    public IActionResult Grades() => View("~/Views/Grades/Index.cshtml");

    // Module Cahier de texte / Journal de classe (ticket JGK-P04) — ClassJournalController garde
    // l'accès (écriture Enseignant sur sa propre séance planifiée, lecture ouverte en plus à
    // Directeur/Secrétariat/Surveillant) et la RLS isole.
    [HttpGet("/cahier-de-texte")]
    public IActionResult ClassJournal() => View("~/Views/ClassJournal/Index.cshtml");

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

    // Module Examens officiels (CFEE/BFEM/BAC) — ExamsController garde l'accès (Directeur/Secrétariat
    // sur tout le module ; lecture des dossiers ouverte en plus à l'Enseignant, borné à ses classes
    // assignées, ticket JGK-J08) et la RLS isole.
    [HttpGet("/examens")]
    public IActionResult Exams() => View("~/Views/Exams/Index.cshtml");

    // Module Intégration étatique (SIMEN / Planète / STATEDUC) — StateIntegrationController garde
    // l'accès (Directeur sur tout ; Secrétariat en plus sur IEN et certificats de mutation) et la RLS
    // isole. Gabarit anonyme comme le reste, l'API porte la garde.
    [HttpGet("/integration-etatique")]
    public IActionResult StateIntegration() => View("~/Views/StateIntegration/Index.cshtml");

    // Vérification PUBLIQUE d'un certificat de mutation (scan du QR imprimé sur la pièce). Anonyme :
    // c'est l'école d'accueil, extérieure à la plateforme, qui arrive ici. L'API sous-jacente
    // (GET /api/v1/state-integration/certificates/verify/{token}) ne révèle aucune donnée d'élève.
    // Le format de cette URL est fixé par GenerateStudentMutationCertificateCommandHandler
    // (BuildVerificationUrl) : ne pas le changer sans migrer les certificats déjà émis.
    [HttpGet("/verifier/mutation/{token}")]
    public IActionResult VerifyMutation(string token)
    {
        ViewData["Token"] = token;
        return View("~/Views/StateIntegration/VerifyMutation.cshtml");
    }

    // JGK-F05 : consolidation des revenus + export comptable .xlsx. Même gabarit anonyme que
    // ci-dessus — l'accès réel est gardé par FinancialReportsController, qui cumule
    // [Authorize(Directeur, Finance)] ET [RequireFeature(AdvancedFinancialReports)].
    // Évolution N°7 : rapports institutionnels (rapport de rentrée IEF, normes d'âge). InstitutionalController
    // garde l'accès (Directeur/Secrétariat) et la RLS isole — gabarit anonyme comme le reste.
    [HttpGet("/rapports/institutionnels")]
    public IActionResult InstitutionalReports() => View("~/Views/Reports/Institutional.cshtml");

    // Évolution N°7 : programmes nationaux (référentiel des chapitres) et avancement pointé au cahier de texte.
    // SyllabusController garde l'accès (écriture Directeur, tableau Directeur/Secrétariat), la RLS isole.
    [HttpGet("/programmes")]
    public IActionResult Syllabus() => View("~/Views/Syllabus/Index.cshtml");

    [HttpGet("/rapports/financiers")]
    public IActionResult FinancialReport() => View("~/Views/Reports/Financial.cshtml");

    // JGK-I03 : espace Super Admin de revue des demandes d'inscription self-service. Comme les autres
    // pages, [AllowAnonymous] côté vue (le JWT ne voyage pas en navigation) — c'est
    // AdminRegistrationRequestsController qui garde l'accès (Roles = SuperAdmin) et la RLS qui isole.
    [HttpGet("/admin/inscriptions")]
    public IActionResult RegistrationRequests() => View("~/Views/Admin/RegistrationRequests.cshtml");

    // Console Super Admin (refonte plateforme) — gabarits [AllowAnonymous] au même titre que le reste
    // de ce contrôleur : c'est SchoolsController (Roles = SuperAdmin) et les futures routes
    // plateforme équivalentes qui gardent l'accès aux données, jamais la page elle-même. Le lien
    // d'entrée dans _Layout.cshtml et l'atterrissage par défaut (wwwroot/js/auth.js,
    // defaultLandingForRole) pointent vers /admin.
    [HttpGet("/admin")]
    public IActionResult SuperAdminDashboard() => View("~/Views/SuperAdmin/Dashboard.cshtml");

    [HttpGet("/admin/etablissements")]
    public IActionResult SuperAdminSchools() => View("~/Views/SuperAdmin/Schools.cshtml");

    [HttpGet("/admin/facturation")]
    public IActionResult SuperAdminBilling() => View("~/Views/SuperAdmin/Billing.cshtml");

    // Module Tarification, Réductions & Offres Promotionnelles — PromoCodesController (Roles =
    // SuperAdmin) garde l'accès aux données, comme le reste de cette console.
    [HttpGet("/admin/tarification")]
    public IActionResult SuperAdminPricing() => View("~/Views/SuperAdmin/Pricing.cshtml");

    [HttpGet("/admin/securite")]
    public IActionResult SuperAdminSecurity() => View("~/Views/SuperAdmin/Security.cshtml");

    // Les années scolaires ne sont plus une entrée de menu à part : elles vivent désormais dans les
    // Paramètres (onglet dédié). On redirige l'ancienne adresse pour ne casser aucun lien existant.
    [HttpGet("/annees-scolaires")]
    public IActionResult SchoolYears() => RedirectToAction(nameof(Settings), new { tab = "annees-scolaires" });

    // ------------------------------------------------------------------ Module Comptabilité & Fiscalité (JGK)

    [HttpGet("/tresorerie")]
    public IActionResult Treasury() => View("~/Views/Treasury/Index.cshtml");

    [HttpGet("/paie")]
    public IActionResult Payroll() => View("~/Views/Payroll/Index.cshtml");

    [HttpGet("/fiscalite")]
    public IActionResult Taxes() => View("~/Views/Taxes/Index.cshtml");

    // ------------------------------------------------------------------ Centre d'aide

    // Guide d'utilisation intégré. Contenu 100 % statique (wwwroot/js/help.js) : aucune donnée
    // d'établissement n'y transite, donc aucun appel d'API à garder ici — et la page reste
    // consultable quand le serveur d'API est injoignable, c'est-à-dire précisément quand
    // l'utilisateur cherche de l'aide. Ouverte à tous les rôles, y compris au Super Admin.
    [HttpGet("/aide")]
    public IActionResult Help() => View("~/Views/Help/Index.cshtml");

    // ------------------------------------------------------------------ Vitrine publique (marketing)

    // Page vitrine B2B : présente le logiciel aux directeurs/gérants d'établissement. Contenu
    // statique, aucun appel API — donc rien à garder côté serveur au-delà de [AllowAnonymous].
    [HttpGet("/vitrine")]
    public IActionResult LandingB2B() => View("~/Views/Home/LandingB2B.cshtml");

    // Catalogue commercial détaillé des 14 modules. Séparé de /vitrine (qui reste orientée
    // conversion) pour que la présentation ne redevienne pas une longue page de documentation.
    // Même contenu source (SamaEcole.Web.Content.MarketingCatalog), rendu côté serveur — statique,
    // aucun appel API.
    [HttpGet("/modules")]
    public IActionResult Modules() => View("~/Views/Home/Modules.cshtml");

    // Annuaire B2C (orientation des familles) — même principe que PublicDirectoryController :
    // vitrine anonyme, en lecture seule. Gabarit statique pour l'instant ; le branchement sur
    // GET /api/v1/public/schools reste à faire (voir PublicDirectoryController).
    [HttpGet("/annuaire")]
    public IActionResult AnnuaireB2C() => View("~/Views/Home/AnnuaireB2C.cshtml");
}
