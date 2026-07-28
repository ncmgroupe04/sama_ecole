using SamaEcole.Application.Common.Interfaces;
using Microsoft.AspNetCore.Http;

namespace SamaEcole.Infrastructure.Multitenancy;

/// <summary>
/// Résout le SchoolId exclusivement depuis le claim JWT "schoolId" — jamais depuis la query
/// string, un header custom ou le body (AGENTS.md règle #10). Un Directeur/Enseignant/etc.
/// ne peut donc jamais forger un SchoolId différent du sien.
///
/// Exception étroite et nommée, réservée aux tâches de fond internes (ex. DebtorAgingHostedService) :
/// <see cref="RunAsSchoolAsync{T}"/> pose un override porté par un <see cref="AsyncLocal{T}"/>. Aucun
/// HttpContext n'existe hors requête — sans lui, CurrentSchoolId resterait null et le Global Query
/// Filter + la policy RLS bloqueraient structurellement toute lecture (comportement voulu, "fail
/// closed" — voir TenantConnectionInterceptor). L'override N'EST JAMAIS accessible depuis une requête
/// HTTP : rien dans SamaEcole.Web ne l'appelle, et il retombe sur le claim JWT dès qu'il n'est pas
/// positionné (retour au comportement par défaut fail-closed dans un `finally`).
/// </summary>
public class TenantProvider(IHttpContextAccessor httpContextAccessor) : ITenantProvider
{
    private static readonly AsyncLocal<Guid?> BackgroundJobOverride = new();

    public Guid? CurrentSchoolId
    {
        get
        {
            if (BackgroundJobOverride.Value is { } overrideSchoolId)
            {
                return overrideSchoolId;
            }

            var claim = httpContextAccessor.HttpContext?.User.FindFirst("schoolId");
            return claim is not null && Guid.TryParse(claim.Value, out var id) ? id : null;
        }
    }

    /// <summary>
    /// Exécute <paramref name="work"/> comme si l'utilisateur courant appartenait à <paramref name="schoolId"/> —
    /// RÉSERVÉ aux tâches de fond qui itèrent plusieurs écoles sans requête HTTP (ex.
    /// DebtorAgingHostedService). L'appelant DOIT ouvrir un scope DI + un DbContext NEUFS pour chaque
    /// école : TenantConnectionInterceptor ne repositionne <c>app.current_school_id</c> qu'à
    /// l'ouverture d'une connexion, jamais deux écoles ne doivent partager la même instance de
    /// connexion sous cet override.
    /// </summary>
    public static async Task<T> RunAsSchoolAsync<T>(Guid schoolId, Func<Task<T>> work)
    {
        BackgroundJobOverride.Value = schoolId;
        try
        {
            return await work();
        }
        finally
        {
            BackgroundJobOverride.Value = null;
        }
    }
}
