using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using SamaEcole.Domain.Enums;
using SamaEcole.Web.Authorization;
using SamaEcole.Web.Controllers;
using Xunit;

namespace SamaEcole.FunctionalTests.Security;

/// <summary>
/// Garde-fou d'ARCHITECTURE de l'isolation par profil : chaque contrôleur de l'API appartient soit à un module
/// (<c>[RequireModule]</c>, interrupteur par école) ou à une fonctionnalité de formule (<c>[RequireFeature]</c>), soit à la
/// liste explicite des contrôleurs TRANSVERSES ci-dessous. Un contrôleur ajouté demain sans l'un ni l'autre fait ÉCHOUER ce
/// test : le développeur doit décider consciemment « ce domaine dépend-il d'un module ? » au lieu de le laisser ouvert à tous
/// les profils par oubli (un Daara se retrouverait avec une API de notes accessible).
///
/// Pas de base de données : pure réflexion sur l'assembly Web.
/// </summary>
public class ModuleCoverageTests
{
    /// <summary>
    /// Contrôleurs volontairement hors de tout module. Une entrée ici est une DÉCISION, à justifier :
    /// socle commun (élèves, inscriptions, classes, enseignants, années, réglages), plateforme / Super Admin,
    /// authentification, webhooks, pages (gabarits anonymes, voir <see cref="PageModuleAttribute"/>).
    /// <c>ClassroomsController</c> est pédagogique dans l'esprit, mais Frais par classe, Paie et Inscriptions en dépendent —
    /// décision du 13/10/2026 : API laissée ouverte, lien « Classes » masqué en Daara — Mémorisation (classesNavVisible).
    /// <c>SchedulesController</c> (emploi du temps) n'est PLUS transverse : il est sous [RequireModule(Pedagogy)].
    /// </summary>
    private static readonly HashSet<string> CrossCuttingControllers =
    [
        // Socle commun
        nameof(StudentsController), nameof(EnrollmentsController), nameof(ClassroomsController), nameof(TeachersController),
        nameof(SchoolYearsController), nameof(SchoolSettingsController), nameof(CurrentSchoolController),
        nameof(BuildingsController), nameof(RoomsController), nameof(InventoryController), nameof(UsersController),
        nameof(AuditLogsController), nameof(FeaturesController), nameof(OnboardingController), nameof(SubscriptionsController),
        // Authentification, compte, inscription publique
        nameof(AuthController), nameof(AccountController), nameof(RegistrationRequestsController), nameof(PublicDirectoryController),
        // Plateforme (Super Admin) et paiements
        nameof(PlatformController), nameof(SchoolsController), nameof(AdminRegistrationRequestsController), nameof(PromoCodesController),
        nameof(PaymentWebhooksController), nameof(SmsWebhooksController), nameof(DevPaymentSimulationController),
        // Gabarits Razor (PageModule côté page, API gardée ailleurs)
        nameof(PagesController)
    ];

    private static IEnumerable<Type> WebControllers() =>
        typeof(PagesController).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(ControllerBase).IsAssignableFrom(t));

    private static bool CarriesModuleOrFeatureGate(Type controller) =>
        HasGate(controller)
        || controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).Any(HasGate);

    private static bool HasGate(MemberInfo member) =>
        member.GetCustomAttributes<RequireModuleAttribute>().Any() || member.GetCustomAttributes<RequireFeatureAttribute>().Any();

    [Fact]
    public void Every_Controller_Is_Gated_By_A_Module_Or_Feature_Or_Declared_Cross_Cutting()
    {
        var unclassified = WebControllers()
            .Where(t => !CarriesModuleOrFeatureGate(t) && !CrossCuttingControllers.Contains(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();

        unclassified.Should().BeEmpty(
            "tout contrôleur doit porter [RequireModule]/[RequireFeature] ou être listé comme transverse dans {0} avec une justification",
            nameof(CrossCuttingControllers));
    }

    [Fact]
    public void The_Schedule_Api_Is_Guarded_By_The_Pedagogy_Module()
    {
        typeof(SchedulesController).GetCustomAttributes<RequireModuleAttribute>().Single().Policy
            .Should().Be(ModulePolicies.NameFor(SchoolModule.Pedagogy));
    }

    [Fact]
    public void A_Cross_Cutting_Controller_Carries_No_Module_Gate()
    {
        // Sans cette symétrie la liste dériverait : un contrôleur gardé ensuite par un module resterait à tort « transverse ».
        var stale = WebControllers()
            .Where(t => CrossCuttingControllers.Contains(t.Name) && CarriesModuleOrFeatureGate(t))
            .Select(t => t.Name)
            .ToList();

        stale.Should().BeEmpty("ces contrôleurs sont gardés par un module : les retirer de la liste des transverses");
    }

    [Fact]
    public void Every_Declared_Cross_Cutting_Controller_Still_Exists()
    {
        var existing = WebControllers().Select(t => t.Name).ToHashSet();

        CrossCuttingControllers.Where(n => !existing.Contains(n)).Should().BeEmpty(
            "une entrée orpheline masquerait un futur contrôleur du même nom");
    }
}
