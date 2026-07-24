using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Common.Behaviors;

/// <summary>
/// Ticket JGK-H01 — journal d'audit centralisé. Journalise automatiquement toute Command/Query qui
/// implémente IAuditableRequest, succès ou échec, sans qu'aucun Handler n'ait à écrire l'entrée
/// lui-même (docs/Volume_7_Security.md §7 : « capture automatique »).
///
/// Exige un tenant déjà établi (tenantProvider.CurrentSchoolId) : la policy RLS de `audit_logs`
/// rejette tout INSERT tenté depuis une session sans SchoolId, quelle que soit la valeur visée — la
/// connexion (JGK-A04, tenant pas encore établi) et les actions Super Admin (aucun SchoolId propre,
/// ex. CreateSchoolCommand) exigeraient donc leur propre fonction SECURITY DEFINER, comme
/// provision_school_director (JGK-B01). Scope volontairement limité, pour cette passe, aux actions
/// post-authentification exécutées par un acteur déjà rattaché à une école : paiements (JGK-F02),
/// statut/mot de passe/création de compte utilisateur (JGK-A05 et extensions), impressions de reçus.
/// </summary>
public class AuditLoggingBehavior<TRequest, TResponse>(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    public async Task<TResponse> Handle(
        TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuditableRequest)
        {
            return await next();
        }

        var actorId = currentUser.UserId;
        if (actorId is null)
        {
            // Ne devrait pas arriver pour les commandes marquées (toutes exigent un rôle authentifié),
            // mais mieux vaut ne rien journaliser qu'écrire une ligne avec un acteur inventé.
            return await next();
        }

        try
        {
            var response = await next();

            await TryAppendAsync(actorId.Value, success: true, failureReason: null, cancellationToken);

            return response;
        }
        catch (Exception ex)
        {
            var truncatedReason = ex.ToString();
            await TryAppendAsync(actorId.Value, success: false, failureReason: truncatedReason, cancellationToken);

            throw;
        }
    }

    private async Task TryAppendAsync(
        Guid actorId, bool success, string? failureReason, CancellationToken cancellationToken)
    {
        // Sans tenant établi (Super Admin, ou tout appelant sans SchoolId propre), la policy RLS
        // rejetterait l'INSERT quelle que soit la valeur visée — rien à journaliser ici pour ce cas
        // (voir la remarque de classe : nécessiterait sa propre fonction SECURITY DEFINER).
        var schoolId = tenantProvider.CurrentSchoolId;

        if (schoolId is null)
        {
            return;
        }

        var (module, action) = DescribeRequest(typeof(TRequest));

        // Truncation pour éviter les PostgresException 22001 (value too long)
        module = module.Length > 50 ? module[..47] + "..." : module;
        action = action.Length > 100 ? action[..97] + "..." : action;
        failureReason = failureReason?.Length > 3950 ? failureReason[..3950] + "..." : failureReason;

        dbContext.AuditLogs.Add(new AuditLog
        {
            SchoolId = schoolId.Value,
            UserId = actorId,
            Module = module,
            Action = action,
            Success = success,
            FailureReason = failureReason,
            IpAddress = currentUser.IpAddress,
            OccurredAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Dérive Module/Action de l'espace de noms et du nom de type — ex.
    /// "SamaEcole.Application.Finance.Commands.RecordPayment.RecordPaymentCommand" → ("Finance", "RecordPayment").
    /// Aucune configuration par Handler à maintenir : le classement suit la seule convention déjà
    /// imposée par la structure des dossiers (docs/Volume_6_Dev_Guide.md §3).
    /// </summary>
    private static (string Module, string Action) DescribeRequest(Type requestType)
    {
        var segments = (requestType.Namespace ?? string.Empty).Split('.');
        var applicationIndex = Array.IndexOf(segments, "Application");
        var module = applicationIndex >= 0 && applicationIndex + 1 < segments.Length
            ? segments[applicationIndex + 1]
            : "Inconnu";

        var action = requestType.Name;
        foreach (var suffix in new[] { "Command", "Query" })
        {
            if (action.EndsWith(suffix, StringComparison.Ordinal))
            {
                action = action[..^suffix.Length];
                break;
            }
        }

        return (module, action);
    }
}
