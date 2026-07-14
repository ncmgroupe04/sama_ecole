# SamaEcole.Persistence

Implémentation EF Core + PostgreSQL (Npgsql uniquement).

- `Configurations/` — une classe `IEntityTypeConfiguration<T>` par entité (jamais de Data Annotations).
- `Migrations/` — générées via `dotnet ef migrations add`. Ne jamais modifier une migration déjà appliquée.
- `Seed/` — chargement de docs/seed-data.json en environnement Development/Test uniquement.

Chaque configuration d'entité tenant doit définir le Global Query Filter (`SchoolId`) ET s'assurer que la policy RLS PostgreSQL correspondante existe dans la migration. Voir AGENTS.md règle #2.
