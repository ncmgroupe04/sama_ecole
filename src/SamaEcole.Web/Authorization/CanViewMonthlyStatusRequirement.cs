using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Exigence « peut consulter la Situation des mensualités » : Directeur toujours ; Finance seulement si le Directeur de
/// son école a ouvert ce droit (SchoolSettings.AllowFinanceToViewMonthlyStatus, faux par défaut) — voir
/// CanViewMonthlyStatusHandler.
/// </summary>
public class CanViewMonthlyStatusRequirement : IAuthorizationRequirement;
