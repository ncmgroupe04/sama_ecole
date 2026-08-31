using System.Data.Common;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace SamaEcole.Persistence.Interceptors;

/// <summary>
/// Positionne <c>app.current_school_id</c> sur CHAQUE connexion ouverte : c'est la variable que
/// lisent les policies RLS PostgreSQL (migration EnableRowLevelSecurity, ticket JGK-A03).
/// Sans elle, une session ne voit AUCUNE ligne des tables tenant — la RLS échoue en fermeture,
/// jamais en ouverture.
///
/// Pourquoi à l'ouverture de connexion et pas une fois pour toutes : Npgsql met les connexions en
/// pool et exécute un DISCARD ALL au retour au pool, ce qui efface le réglage. Le repositionner à
/// chaque ouverture est donc à la fois nécessaire et suffisant — et c'est ce qui garantit qu'une
/// connexion recyclée ne conserve jamais le tenant de la requête précédente.
///
/// Le SchoolId vient de ITenantProvider, donc du claim JWT, jamais d'un paramètre client (règle #10).
/// </summary>
public class TenantConnectionInterceptor(ITenantProvider tenantProvider) : DbConnectionInterceptor
{
    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = BuildApplyTenantCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        // Chemin d'ouverture SYNCHRONE d'EF Core. On exécute réellement la commande en synchrone
        // (ExecuteNonQuery) plutôt qu'un .GetAwaiter().GetResult() sur la variante async : bloquer un
        // thread du pool sur une E/S base, à CHAQUE ouverture de connexion, expose à la famine de
        // threads sous charge. set_config sur une connexion déjà ouverte est une opération synchrone
        // légitime — aucune raison d'y faire tourner une machine à états asynchrone.
        using var command = BuildApplyTenantCommand(connection);
        command.ExecuteNonQuery();
        base.ConnectionOpened(connection, eventData);
    }

    /// <summary>
    /// Compose l'unique instruction posée sur chaque connexion : <c>set_config('app.current_school_id', …)</c>.
    /// Hors requête authentifiée (login, migrations, tâches de fond) il n'y a pas de tenant : on pose la
    /// chaîne vide. Les policies la traduisent en NULL (NULLIF) et ne laissent donc passer aucune ligne,
    /// au lieu de faire échouer le cast en uuid. Le SchoolId vient de ITenantProvider — du claim JWT,
    /// jamais d'un paramètre client (règle #10) — et transite en PARAMÈTRE, jamais par interpolation.
    /// </summary>
    private DbCommand BuildApplyTenantCommand(DbConnection connection)
    {
        var schoolId = tenantProvider.CurrentSchoolId?.ToString() ?? string.Empty;

        var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.current_school_id', @schoolId, false)";

        var parameter = command.CreateParameter();
        parameter.ParameterName = "schoolId";
        parameter.Value = schoolId;
        command.Parameters.Add(parameter);

        return command;
    }
}