using Microsoft.AspNetCore.Authorization;

namespace SamaEcole.Web.Authorization;

/// <summary>
/// Remplace la comparaison manuelle « SchoolId de l'URL vs claim JWT » recopiée dans plusieurs handlers
/// MediatR (ex. InitiateSubscriptionPaymentHandler, GetSubscriptionPaymentsQueryHandler — ticket
/// d'audit BOLA/IDOR sur les abonnements). Réutilisable pour TOUT contrôleur dont l'action reçoit un
/// <c>{schoolId:guid}</c> en segment de route : voir SchoolResourceAuthorizationHandler pour la règle
/// et SchoolResourcePolicies pour le nom de policy à utiliser.
/// </summary>
public class SchoolResourceRequirement : IAuthorizationRequirement;
