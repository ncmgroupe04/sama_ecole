using SamaEcole.Infrastructure.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Guichet PayDunya SIMULÉ — n'existe que pour débloquer le test du parcours d'abonnement en
/// développement local sans compte marchand PayDunya (voir DevPaymentService). [AllowAnonymous] : un
/// vrai guichet PayDunya n'a pas non plus de session Unikol.
///
/// Repli défensif même si DevPaymentService n'est branché comme IPaymentService qu'en Development
/// (DependencyInjection) : NotFound explicite si l'hôte n'est pas Development, au cas où cette route
/// serait atteinte ailleurs — elle n'affiche jamais rien qu'un attaquant pourrait exploiter (aucune
/// écriture, TryGetPending échoue simplement si rien n'a été initié via DevPaymentService).
/// </summary>
[Route("dev/paydunya-checkout")]
[AllowAnonymous]
public class DevPaymentSimulationController(IWebHostEnvironment environment, DevPaymentService devPaymentService)
    : Controller
{
    [HttpGet("{reference}")]
    public IActionResult Checkout(string reference)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        if (!devPaymentService.TryGetPending(reference, out var payment))
        {
            return NotFound();
        }

        return View(new DevPaydunyaCheckoutViewModel(
            reference, payment.InternalPaymentId, payment.Amount, payment.Description));
    }
}

public record DevPaydunyaCheckoutViewModel(string Reference, Guid InternalPaymentId, decimal Amount, string Description);
