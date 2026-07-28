using FluentAssertions;
using SamaEcole.Domain.Common;
using SamaEcole.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace SamaEcole.IntegrationTests.Multitenancy;

/// <summary>
/// Garde-fou générique de l'isolation — AGENTS.md règle #2.
///
/// La règle exige DEUX protections sur toute table tenant : le Global Query Filter EF Core ET une
/// policy RLS PostgreSQL. Le filtre EF est posé automatiquement pour toute ITenantEntity
/// (ApplicationDbContext.OnModelCreating) : impossible de l'oublier. La policy RLS, elle, s'écrit à
/// la main dans une migration — et rien ne signalait jusqu'ici qu'on l'avait oubliée. Une table
/// protégée par le seul filtre EF laisse fuir les données de toutes les écoles dès la première
/// requête SQL brute, ou dès qu'un `IgnoreQueryFilters()` traîne quelque part.
///
/// Ce test n'énumère aucune liste figée : il interroge le MODÈLE EF. Toute entité tenant ajoutée
/// demain sans sa policy fera échouer la CI d'elle-même, sans que personne n'ait à penser à mettre
/// une liste à jour.
/// </summary>
[Trait("Category", "MultiTenant")]
public class RlsCoverageTests : IAsyncLifetime
{
    private readonly RlsTestDatabase _db = new();

    public Task InitializeAsync() => _db.InitializeAsync();

    public Task DisposeAsync() => _db.DisposeAsync().AsTask();

    [Fact]
    public async Task Every_Tenant_Table_Must_Have_Row_Level_Security_Enabled_And_A_Policy()
    {
        var tenantTables = TenantTableNames();

        tenantTables.Should().NotBeEmpty("le modèle doit comporter des entités tenant — sinon ce test ne teste rien");

        var unprotected = new List<string>();

        await using var connection = new NpgsqlConnection(_db.AppConnectionString);
        await connection.OpenAsync();

        foreach (var table in tenantTables)
        {
            await using var command = connection.CreateCommand();

            // relrowsecurity : la RLS est-elle ACTIVE sur la table ?
            // pg_policies    : existe-t-il au moins une policy ? Une RLS active sans policy bloque
            //                  tout, et une policy sans RLS active ne s'applique jamais — il faut
            //                  les deux, et aucune des deux vérifications ne remplace l'autre.
            command.CommandText =
                """
                SELECT c.relrowsecurity,
                       (SELECT count(*) FROM pg_policies p
                         WHERE p.schemaname = 'public' AND p.tablename = @table)
                FROM pg_class c
                JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relname = @table;
                """;
            command.Parameters.AddWithValue("table", table);

            await using var reader = await command.ExecuteReaderAsync();

            if (!await reader.ReadAsync())
            {
                unprotected.Add($"{table} (table absente de la base)");
                continue;
            }

            var rlsEnabled = reader.GetBoolean(0);
            var policyCount = reader.GetInt64(1);

            if (!rlsEnabled || policyCount == 0)
            {
                unprotected.Add($"{table} (RLS active : {rlsEnabled}, policies : {policyCount})");
            }
        }

        unprotected.Should().BeEmpty(
            "toute table tenant doit porter une policy RLS en plus du filtre EF Core (AGENTS.md règle #2) — " +
            "ajoutez-la dans la migration qui crée la table");
    }

    /// <summary>Les tables des entités qui portent un SchoolId, lues dans le modèle EF lui-même.</summary>
    private List<string> TenantTableNames()
    {
        using var context = _db.NewOwnerContext();

        return context.Model.GetEntityTypes()
            .Where(entity => typeof(ITenantEntity).IsAssignableFrom(entity.ClrType))
            .Select(entity => entity.GetTableName()!)
            .Distinct()
            .OrderBy(name => name)
            .ToList();
    }
}
