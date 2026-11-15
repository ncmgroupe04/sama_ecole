using System.Reflection;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using SamaEcole.Web.Controllers;
using Xunit;

namespace SamaEcole.FunctionalTests.Security;

/// <summary>
/// Garde de dérive de l'annexe <c>docs/Volume_7_Annexe_Matrice_Routes.md</c> : la matrice des droits de CHAQUE route API
/// est générée par réflexion sur les contrôleurs (<c>[Authorize(Roles)]</c>, <c>[RequireModule]</c>, <c>[RequireFeature]</c>,
/// politiques de délégation) et comparée au fichier versionné. Changer le rôle autorisé d'une route sans régénérer
/// l'annexe fait échouer ce test : la documentation des droits ne peut plus diverger du code.
///
/// Régénérer : <c>UPDATE_PERMISSION_MATRIX=1 dotnet test tests/SamaEcole.FunctionalTests --filter PermissionMatrix</c>.
/// </summary>
public class PermissionMatrixTests
{
    private const string DocRelativePath = "docs/Volume_7_Annexe_Matrice_Routes.md";

    private static readonly Dictionary<string, string> PolicyLabels = new()
    {
        ["Module:Pedagogy"] = "module Pédagogie",
        ["Module:Finance"] = "module Finance",
        ["Module:Internat"] = "module Internat",
        ["Module:Coran"] = "module Coran",
        ["Feature:AdvancedFinancialReports"] = "formule Standard ou supérieure (rapports avancés)",
        ["Feature:SmsNotifications"] = "formule Premium (SMS)",
        ["CanDeleteFees"] = "délégation « la Finance peut supprimer des frais »",
        ["CanModifyFees"] = "délégation « la Finance peut modifier les montants de frais »",
        ["CanManageGradingScale"] = "délégation « le Secrétariat gère la configuration des notes »",
        ["CanViewMonthlyStatus"] = "accès « situation des mensualités » (Finance, si le Directeur l'ouvre)",
        ["CanCancelPayslips"] = "droit d'annulation des fiches de paie (Finance, si le Directeur l'accorde)"
    };

    /// <summary>
    /// Politiques de délégation : le rôle est décidé par le Handler (Directeur toujours, un second rôle seulement si le Directeur
    /// a ouvert la délégation). Sans cette table, ces routes s'afficheraient « Tout utilisateur authentifié », ce qui est faux.
    /// </summary>
    private static readonly Dictionary<string, string> PolicyRoles = new()
    {
        ["CanManageGradingScale"] = "Directeur ; Secretariat si délégué",
        ["CanModifyFees"] = "Directeur ; Finance si délégué",
        ["CanDeleteFees"] = "Directeur ; Finance si délégué",
        ["CanViewMonthlyStatus"] = "Directeur ; Finance si accès ouvert",
        ["CanCancelPayslips"] = "Directeur ; Finance si droit accordé"
    };

    [Fact]
    public void The_Committed_Annex_Matches_The_Controllers()
    {
        var generated = Generate();
        var path = Path.Combine(RepositoryRoot(), DocRelativePath);

        if (Environment.GetEnvironmentVariable("UPDATE_PERMISSION_MATRIX") == "1")
        {
            File.WriteAllText(path, generated, new UTF8Encoding(false));
            return;
        }

        File.Exists(path).Should().BeTrue($"{DocRelativePath} doit exister (UPDATE_PERMISSION_MATRIX=1 pour le générer)");
        var committed = File.ReadAllText(path).Replace("\r\n", "\n");

        committed.Should().Be(
            generated,
            $"{DocRelativePath} est générée depuis les contrôleurs : régénérez-la avec UPDATE_PERMISSION_MATRIX=1 après tout changement de droits");
    }

    [Fact]
    public void No_Api_Route_Is_Left_Without_An_Explicit_Authorization_Decision()
    {
        // Une route sans aucun [Authorize] ni [AllowAnonymous] (ni sur la classe ni sur la méthode) serait ouverte par oubli.
        var offenders = ApiActions()
            .Where(a => !a.Anonymous && a.AuthorizeAttributes.Count == 0)
            .Select(a => $"{a.Method} {a.Route}")
            .ToList();

        offenders.Should().BeEmpty("toute route doit déclarer [Authorize] ou [AllowAnonymous] explicitement");
    }

    private static string Generate()
    {
        var sb = new StringBuilder();
        sb.Append("# Volume 7 — Annexe : matrice des droits par route API\n\n");
        sb.Append("> **Fichier généré** par `tests/SamaEcole.FunctionalTests/Security/PermissionMatrixTests.cs` à partir des attributs des\n");
        sb.Append("> contrôleurs — ne pas l'éditer à la main. Un changement de droits sans régénération fait échouer les tests.\n");
        sb.Append("> Régénérer : `UPDATE_PERMISSION_MATRIX=1 dotnet test tests/SamaEcole.FunctionalTests --filter PermissionMatrix`.\n\n");
        sb.Append("Lecture : **Rôles** = rôles autorisés (une intersection `A ∩ B` signale une garde de classe et une garde de méthode\n");
        sb.Append("cumulées : il faut satisfaire les deux). « Tout utilisateur authentifié » = `[Authorize]` sans rôle ; le serveur borne\n");
        sb.Append("alors par la RLS et les règles du Handler. **Condition** = module activé, formule d'abonnement ou délégation exigée\n");
        sb.Append("en plus du rôle. Les routes anonymes sont listées pour mémoire (connexion, webhooks signés, annuaire public).\n");
        sb.Append("Le détail fonctionnel par module reste dans la matrice du §15 de `Volume_7_Security.md`.\n\n");

        foreach (var group in ApiActions().GroupBy(a => a.Controller).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            sb.Append($"## {group.Key}\n\n| Méthode | Route | Rôles | Condition |\n|---|---|---|---|\n");
            foreach (var a in group.OrderBy(x => x.Route, StringComparer.Ordinal).ThenBy(x => x.Method, StringComparer.Ordinal))
            {
                var roles = a.Anonymous ? "_anonyme_" : a.Roles.Length == 0 ? "Tout utilisateur authentifié" : a.Roles;
                sb.Append($"| {a.Method} | `{a.Route}` | {roles} | {a.Condition} |\n");
            }

            sb.Append('\n');
        }

        return sb.ToString();
    }

    private sealed record ApiAction(
        string Controller, string Method, string Route, bool Anonymous, string Roles, string Condition,
        IReadOnlyList<AuthorizeAttribute> AuthorizeAttributes);

    private static List<ApiAction> ApiActions()
    {
        var result = new List<ApiAction>();
        var assembly = typeof(PagesController).Assembly;

        foreach (var type in assembly.GetTypes()
                     .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t != typeof(PagesController)))
        {
            var routeTemplate = type.GetCustomAttribute<RouteAttribute>()?.Template ?? string.Empty;
            var classAuthorize = type.GetCustomAttributes<AuthorizeAttribute>(true).ToList();
            var classAnonymous = type.GetCustomAttribute<AllowAnonymousAttribute>() is not null;
            var controllerName = type.Name.Replace("Controller", string.Empty);

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                var http = method.GetCustomAttributes().OfType<HttpMethodAttribute>().FirstOrDefault();
                if (http is null) continue;

                var template = http.Template ?? string.Empty;
                var route = ("/" + routeTemplate.Trim('/') + (template.Length == 0 ? string.Empty : "/" + template.TrimStart('/')))
                    .Replace("{controller}", controllerName).Replace("//", "/");

                var authorize = classAuthorize.Concat(method.GetCustomAttributes<AuthorizeAttribute>(true)).ToList();
                var anonymous = classAnonymous || method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                var roleParts = authorize.Select(a => a.Roles).Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!.Replace(",", ", ")).ToList();
                roleParts.AddRange(authorize.Select(a => a.Policy).Where(p => p is not null && PolicyRoles.ContainsKey(p)).Select(p => PolicyRoles[p!]));
                var roles = string.Join(" ∩ ", roleParts);

                var conditions = authorize.Select(a => a.Policy).Where(p => !string.IsNullOrWhiteSpace(p))
                    .SelectMany(p => p!.Split(','))
                    .Concat(Policies(method, type, "RequireModuleAttribute"))
                    .Concat(Policies(method, type, "RequireFeatureAttribute"))
                    .Distinct()
                    .Select(p => PolicyLabels.GetValueOrDefault(p, p))
                    .OrderBy(p => p, StringComparer.Ordinal);

                result.Add(new ApiAction(controllerName, http.HttpMethods.First(), route, anonymous, roles, string.Join(" ; ", conditions), authorize));
            }
        }

        return result;
    }

    private static IEnumerable<string> Policies(MethodInfo method, Type type, string attributeName) =>
        method.GetCustomAttributes().Concat(type.GetCustomAttributes())
            .Where(a => a.GetType().Name == attributeName)
            .Select(a => a.GetType().GetProperty("Policy")?.GetValue(a)?.ToString())
            .Where(p => !string.IsNullOrWhiteSpace(p))!;

    private static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SamaEcole.sln"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("SamaEcole.sln introuvable depuis " + AppContext.BaseDirectory);
    }
}
