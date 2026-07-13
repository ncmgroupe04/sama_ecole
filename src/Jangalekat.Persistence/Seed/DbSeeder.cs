namespace Jangalekat.Persistence.Seed;

/// <summary>
/// Charge docs/seed-data.json — Development/Test UNIQUEMENT, jamais appelé en Production
/// (vérifier IWebHostEnvironment.IsProduction() côté appelant, Jangalekat.Web/Program.cs).
/// Implémentation à compléter avec le premier ticket qui en a besoin (ex. JGK-D01 pour tester
/// l'isolation multi-tenant avec les deux écoles du jeu de données).
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(ApplicationDbContext dbContext, CancellationToken cancellationToken = default)
    {
        // TODO : désérialiser docs/seed-data.json et insérer Schools/Users/Students/... si la
        // base est vide. Respecter l'ordre des dépendances (Schools avant Users avant Students...).
        await Task.CompletedTask;
    }
}
