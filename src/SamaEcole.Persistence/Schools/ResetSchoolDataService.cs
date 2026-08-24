using System.Data.Common;
using SamaEcole.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Persistence.Schools;

/// <summary>
/// « Remise à neuf » d'un établissement : efface les données SAISIES pendant les tests, conserve tout
/// ce qui a été CONFIGURÉ.
///
/// Ce service ne supprime rien lui-même : il APPELLE la fonction PostgreSQL <c>reset_school_data</c>
/// (migration AddSchoolDataReset), et c'est délibéré. Le rôle applicatif n'a aucun droit de DELETE
/// sur les tables métier — chaque migration lui accorde « SELECT, INSERT, UPDATE, jamais DELETE »
/// pour que la règle #6 d'AGENTS.md soit tenue par la BASE et pas seulement par le code. Un
/// <c>ExecuteDeleteAsync</c> émis d'ici serait donc refusé en 42501, et la seule façon de le faire
/// passer aurait été d'accorder le DELETE à l'application : on aurait affaibli la garantie pour tout
/// le produit, définitivement, au bénéfice d'un unique écran.
///
/// La fonction, elle, ne peut faire qu'une chose, sur la seule école de la session (elle compare
/// <c>p_school_id</c> à <c>app.current_school_id</c> et refuse toute divergence). Les tables purgées
/// et leur ordre y sont écrits une fois pour toutes ; ce fichier n'a pas à les connaître.
///
/// Le passage par la base atteint aussi les lignes en SUPPRESSION LOGIQUE : le Global Query Filter
/// d'EF Core (tenant + <c>IsDeleted</c>) ne s'applique pas au SQL, et une école « à neuf » ne doit
/// garder aucune ligne — pas même invisible.
///
/// EXCEPTION ASSUMÉE à la règle #6, bornée à ce chemin : déclenchée par le seul Directeur, sur SON
/// école, après saisie d'un mot de confirmation, et journalisée (ResetSchoolDataCommand est
/// IAuditableRequest — le journal d'audit, lui, n'est pas purgé).
/// </summary>
public class ResetSchoolDataService(
    ApplicationDbContext dbContext,
    ILogger<ResetSchoolDataService> logger)
    : IResetSchoolDataService
{
    public async Task<SchoolDataResetSummary> ResetAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // Passer par la connexion d'EF Core, et non par une NpgsqlConnection montée à la main : c'est
        // son ouverture qui déclenche TenantConnectionInterceptor, donc le positionnement de
        // app.current_school_id — la valeur même que la fonction exige de retrouver. Une connexion
        // ouverte à côté n'aurait aucun tenant et se ferait refuser.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);

        try
        {
            var entries = new List<SchoolDataResetEntry>();

            await using var command = dbContext.Database.GetDbConnection().CreateCommand();

            // Un seul appel = un seul ordre SQL : PostgreSQL le rend atomique de lui-même. Si une
            // contrainte cède au milieu, tout est annulé — jamais d'établissement à moitié vidé.
            command.CommandText = "SELECT label, rows_deleted FROM reset_school_data($1)";
            command.Transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(CreateSchoolIdParameter(command, schoolId));

            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                while (await reader.ReadAsync(cancellationToken))
                {
                    entries.Add(new SchoolDataResetEntry(reader.GetString(0), reader.GetInt32(1)));
                }
            }

            var total = entries.Sum(e => e.RowsDeleted);

            // LogWarning et non LogInformation : une purge irréversible doit ressortir dans les
            // journaux d'exploitation sans avoir à les filtrer.
            logger.LogWarning(
                "Purge des données de l'établissement {SchoolId} : {TotalRows} lignes effacées ({Detail}).",
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
    /// Paramètre POSITIONNEL ($1) : une fonction plpgsql appelée par un DbCommand brut n'accepte pas
    /// les paramètres nommés de Npgsql, et le passer en dur dans le texte de la commande ouvrirait la
    /// porte à une injection sur l'opération la plus destructrice du produit.
    /// </summary>
    private static DbParameter CreateSchoolIdParameter(DbCommand command, Guid schoolId)
    {
        var parameter = command.CreateParameter();
        parameter.Value = schoolId;

        return parameter;
    }
}
