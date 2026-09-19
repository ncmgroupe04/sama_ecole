using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// POST /schools/current/revert-to-test — repasse l'établissement COURANT du mode réel au mode test,
/// pour que le Directeur garde le contrôle de son environnement : continuer à se familiariser avec la
/// plateforme, ou refaire des essais. Action DÉLIBÉRÉE, réservée au Directeur, confirmée par saisie de
/// « TEST » (ou du nom de l'école) — même patron que GoLiveCommand.
///
/// ROUVRE la « Zone de danger » (sauf verrou définitif) : seul <c>WentLiveAt</c> (l'horodatage
/// COURANT) est remis à <c>null</c> — ce qui permet de rejouer <c>GoLiveCommand</c> ET retrouve la
/// purge, puisque <c>ResetSchoolDataCommandHandler</c> ET la fonction PostgreSQL
/// <c>reset_school_data</c> ne se ferment plus que sur <c>School.IsProductionLocked</c>, jamais
/// touché ici. Si le Directeur veut empêcher ce retour de rouvrir la purge, c'est
/// <c>LockProductionCommand</c> — une action manuelle et distincte — qu'il doit invoquer.
///
/// IAuditableRequest : le journal d'audit garde la trace de chaque retour en mode test (module
/// « Schools », action « RevertToTest »).
/// </summary>
public record RevertToTestCommand(string Confirmation)
    : IRequest<RevertToTestResult>, IAuditableRequest;

/// <param name="WasLive">Vrai si l'établissement était effectivement en mode réel avant l'appel.</param>
public sealed record RevertToTestResult(bool WasLive);
