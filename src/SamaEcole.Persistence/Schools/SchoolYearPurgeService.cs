using System.Data.Common;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Persistence.Schools;

/// <summary>
/// Suppression d'une année scolaire et de tout ce qu'elle porte, EN MODE TEST — jumeau de
/// <see cref="ResetSchoolDataService"/>, dont il partage mot pour mot les raisons d'être : le rôle
/// applicatif n'a aucun droit de DELETE sur les tables métier (la règle #6 d'AGENTS.md est tenue par
/// la BASE), le SQL atteint aussi les lignes en suppression logique que le Global Query Filter
/// masque, et l'ordre des clés étrangères vit dans la fonction PostgreSQL, pas ici.
///
/// La fonction <c>delete_school_year</c> (migration AddSchoolYearDeletion) porte ses propres gardes —
/// tenant, appartenance de l'année à l'école, mode test — puisqu'un SECURITY DEFINER est exempté de
/// RLS.
/// </summary>
public class SchoolYearPurgeService(
    ApplicationDbContext dbContext,
    ILogger<SchoolYearPurgeService> logger)
    : ISchoolYearPurgeService
{
    public async Task<SchoolDataResetSummary> PurgeAsync(
        Guid schoolId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        // Par la connexion d'EF Core, et non une NpgsqlConnection montée à la main : c'est son
        // ouverture qui déclenche TenantConnectionInterceptor, donc app.current_school_id — la valeur
        // même que la fonction exige de retrouver.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var entries = new List<SchoolDataResetEntry>();

            await using var command = dbContext.Database.GetDbConnection().CreateCommand();

            command.CommandText = "SELECT label, rows_deleted FROM delete_school_year($1, $2)";

            // La transaction ouverte par le Handler (ExecuteInTransactionAsync) englobe cet appel ET
            // la rebascule de l'année active : sans elle, un échec de la seconde laisserait
            // l'établissement sans exercice de travail.
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(CreateGuidParameter(command, schoolId));
            command.Parameters.Add(CreateGuidParameter(command, schoolYearId));

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    entries.Add(new SchoolDataResetEntry(reader.GetString(0), reader.GetInt32(1)));
                }
            }

            var total = entries.Sum(e => e.RowsDeleted);

            // LogWarning, comme la purge d'établissement : une suppression irréversible doit ressortir
            // des journaux d'exploitation sans avoir à les filtrer.
            logger.LogWarning(
                "Suppression de l'année scolaire {SchoolYearId} de l'établissement {SchoolId} : {TotalRows} lignes effacées ({Detail}).",
                schoolYearId,
                schoolId,
                total,
                string.Join(", ", entries.Where(e => e.RowsDeleted > 0).Select(e => $"{e.Label}={e.RowsDeleted}")));

            return new SchoolDataResetSummary(total, entries);
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// Paramètres POSITIONNELS ($1, $2) : une fonction plpgsql appelée par un DbCommand brut n'accepte
    /// pas les paramètres nommés de Npgsql, et interpoler les identifiants dans le texte de la commande
    /// ouvrirait une injection sur une opération destructrice.
    /// </summary>
    private static DbParameter CreateGuidParameter(DbCommand command, Guid value)
    {
        var parameter = command.CreateParameter();
        parameter.Value = value;

        return parameter;
    }
}
