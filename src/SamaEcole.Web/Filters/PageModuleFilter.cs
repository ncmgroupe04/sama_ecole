using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using SamaEcole.Web.Authorization;

namespace SamaEcole.Web.Filters;

/// <summary>
/// Filtre MVC global : recopie le module déclaré par <see cref="PageModuleAttribute"/> dans <c>ViewData["RequiredModule"]</c>,
/// que <c>_Layout</c> émet en balise <c>required-module</c>. Sans effet (et sans coût) sur les actions sans attribut, dont toute
/// l'API : il ne lit l'attribut que pour un contrôleur qui rend des vues.
/// </summary>
public sealed class PageModuleFilter : IActionFilter
{
    public const string ViewDataKey = "RequiredModule";

    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.Controller is not Controller controller
            || context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            return;
        }

        var attribute = descriptor.MethodInfo.GetCustomAttribute<PageModuleAttribute>();
        if (attribute is not null)
        {
            controller.ViewData[ViewDataKey] = attribute.Module.ToString();
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
