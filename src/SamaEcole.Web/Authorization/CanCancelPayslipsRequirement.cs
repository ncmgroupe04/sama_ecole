using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Exigence « peut annuler ou corriger une fiche de paie » : Directeur toujours ; Finance seulement si le Directeur de
/// son école n'a pas retiré ce droit (SchoolSettings.AllowFinanceToCancelPayslips, vrai par défaut) — voir
/// CanCancelPayslipsHandler.
/// </summary>
public class CanCancelPayslipsRequirement : IAuthorizationRequirement;
