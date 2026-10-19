using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Exigence « peut annuler un versement » : Directeur toujours ; Finance seulement si le Directeur de son école n'a pas
/// retiré ce droit (SchoolSettings.AllowFinanceToCancelPayments, vrai par défaut) — voir CanCancelPaymentsHandler.
/// </summary>
public class CanCancelPaymentsRequirement : IAuthorizationRequirement;
