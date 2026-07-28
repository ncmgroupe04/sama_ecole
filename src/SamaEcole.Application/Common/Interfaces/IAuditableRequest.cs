namespace SamaEcole.Application.Common.Interfaces;

/// <summary>
/// Marqueur (aucun membre) : une Command ou Query qui l'implémente est journalisée automatiquement
/// par AuditLoggingBehavior (ticket JGK-H01). Volontairement OPT-IN plutôt que « toute écriture » :
/// le ticket parle d'« écriture SENSIBLE » (paiements, changements de statut, mot de passe, actions
/// Super Admin, impressions/exports) — auditer aussi CreateClassroomCommand ou ApplyStandardFeeCommand
/// noierait le journal dans du bruit sans valeur de sécurité.
/// </summary>
public interface IAuditableRequest
{
}
