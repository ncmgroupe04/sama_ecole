using Xunit;

namespace Jangalekat.IntegrationTests.Multitenancy;

/// <summary>
/// Test obligatoire référencé par AGENTS.md, .cursor/rules/database.mdc et
/// docs/Volume_8_Test_Strategy.md §5. Doit s'exécuter dans .github/workflows/ci.yml
/// via `dotnet test --filter Category=MultiTenant`.
///
/// Principe : prouver qu'AUCUNE requête ne peut retourner une donnée d'une autre école,
/// même en simulant l'oubli du Global Query Filter EF Core côté C# (ex. requête SQL brute).
/// Seule la policy RLS PostgreSQL doit alors faire foi.
///
/// Implémentation à compléter avec Testcontainers.PostgreSql (voir csproj) : démarrer un
/// conteneur Postgres réel, appliquer les migrations, insérer un élève pour l'École A et un
/// pour l'École B (docs/seed-data.json), puis vérifier qu'une session positionnée sur le
/// tenant A ne voit jamais la ligne de l'École B, y compris via SQL direct.
/// </summary>
[Trait("Category", "MultiTenant")]
public class StudentIsolationTests
{
    [Fact(Skip = "À implémenter avec le ticket JGK-A03 (docs/BACKLOG_TICKETS.md)")]
    public void RawSqlQuery_Should_Never_Return_Other_School_Data_Even_Without_EfCore_Filter()
    {
        // Arrange : conteneur Postgres + migrations + seed deux écoles distinctes
        // Act : SET app.current_school_id = '<École A>', puis SELECT * FROM students SANS passer par EF Core
        // Assert : aucune ligne de l'École B ne doit apparaître — c'est la RLS qui doit bloquer, pas le code C#.
    }
}
