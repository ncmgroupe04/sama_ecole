using MediatR;

namespace SamaEcole.Application.Registration.Commands.RejectRegistrationRequest;

/// <summary>
/// Ticket JGK-I03 — rejet d'une demande d'inscription par le Super Admin. À la différence de
/// l'approbation, aucune école n'est créée : la demande passe simplement en Rejected et conserve le
/// <c>motif</c> obligatoire, seul canal par lequel le Directeur apprendra la raison (affiché par le
/// suivi public JGK-I02).
///
/// <c>IRequest&lt;Unit&gt;</c> plutôt que le marqueur non générique <c>IRequest</c> : ValidationBehavior
/// et AuditLoggingBehavior exigent tous deux <c>TRequest : IRequest&lt;TResponse&gt;</c> — un
/// <c>IRequest</c> simple ne satisfait PAS cette contrainte (il n'hérite pas de IRequest&lt;Unit&gt; au
/// niveau du type), et le conteneur DI omet alors SILENCIEUSEMENT ces comportements du pipeline plutôt
/// que d'échouer bruyamment. Sans ce détail, le motif de rejet obligatoire ne serait jamais vérifié.
/// </summary>
public record RejectRegistrationRequestCommand(Guid Id, string Reason) : IRequest<Unit>;
