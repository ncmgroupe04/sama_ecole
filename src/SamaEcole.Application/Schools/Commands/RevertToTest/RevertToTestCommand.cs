using SamaEcole.Application.Common.Interfaces;
using MediatR;

namespace SamaEcole.Application.Schools.Commands.RevertToTest;

/// <summary>
/// POST /schools/current/dev/revert-to-test — porte de SORTIE réservée aux environnements jetables
/// (dev / recette / staging) : annule le passage en mode réel pour pouvoir rejouer la bascule.
///
/// Aucune entrée, aucune confirmation : c'est un outil de recette, pas une action métier. Sa
/// disponibilité tient à <see cref="ISandboxModeProvider.RevertToTestEnabled"/> — en vraie
/// production, l'endpoint n'est même pas monté (404) et le Handler refuse malgré tout (double garde).
///
/// IAuditableRequest : le journal d'audit garde la trace même d'un retour en arrière de recette
/// (module « Schools », action « RevertToTest »).
/// </summary>
public record RevertToTestCommand
    : IRequest<RevertToTestResult>, IAuditableRequest;

/// <param name="WasLive">Vrai si l'établissement était effectivement en mode réel avant l'appel.</param>
public sealed record RevertToTestResult(bool WasLive);
