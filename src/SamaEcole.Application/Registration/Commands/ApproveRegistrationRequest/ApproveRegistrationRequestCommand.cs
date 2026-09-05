using MediatR;
using SubStatus = SamaEcole.Domain.Enums.SubscriptionStatus;

namespace SamaEcole.Application.Registration.Commands.ApproveRegistrationRequest;

/// <summary>
/// Ticket JGK-I03 — approbation d'une demande d'inscription self-service par le Super Admin
/// (docs/Volume_1_Cahier_des_Charges.md §11.5 étape 3). L'approbation CRÉE, dans UNE SEULE transaction :
/// l'établissement (Active), le compte Directeur (Active, mot de passe déjà choisi à la soumission I01),
/// et l'abonnement (AwaitingPayment, sans date d'expiration). Tout passe ou rien n'est créé.
///
/// La demande, elle, n'est jamais transformée en école : elle reste un historique immuable, seul son
/// statut passe à Approved et <c>CreatedSchoolId</c> pointe vers l'école née de l'approbation
/// (docs/Volume_3_DDS.md §5.7).
/// </summary>
public record ApproveRegistrationRequestCommand(Guid Id) : IRequest<ApproveRegistrationRequestResult>;

/// <summary>
/// Ne renvoie que des identifiants : aucun mot de passe (le Directeur a choisi le sien à la soumission
/// et se connecte avec, il n'y a rien à lui transmettre de neuf).
///
/// <see cref="EmailSent"/> : l'établissement/compte/abonnement sont créés que l'e-mail parte ou non
/// (un échec d'envoi ne doit pas remettre en cause l'approbation, voir le Handler) — ce champ permet
/// au Super Admin de savoir s'il doit prévenir le Directeur par un autre canal.
/// </summary>
public record ApproveRegistrationRequestResult(
    Guid SchoolId,
    Guid DirectorUserId,
    Guid SubscriptionId,
    SubStatus SubscriptionStatus,
    bool EmailSent);
