# Migrations

Ce dossier est **vide intentionnellement** : les migrations sont générées via
`dotnet ef migrations add <Nom> -p src/SamaEcole.Persistence -s src/SamaEcole.Web`
(commande listée dans `AGENTS.md`). Ne jamais les écrire à la main, ne jamais modifier
une migration déjà appliquée.

## Chaque migration qui crée une table `ITenantEntity` doit ajouter la policy RLS correspondante

EF Core ne génère pas les policies RLS automatiquement : elles doivent être ajoutées à la main
dans le fichier de migration généré, via `migrationBuilder.Sql(...)`. Exemple pour `students` :

```sql
ALTER TABLE students ENABLE ROW LEVEL SECURITY;

CREATE POLICY tenant_isolation_students ON students
    USING (school_id = current_setting('app.current_school_id')::uuid);
```

Et côté application, à chaque requête (middleware ASP.NET Core, avant tout accès DbContext) :

```sql
SET app.current_school_id = '<schoolId du JWT>';
```

Voir `docs/Volume_3_DDS.md` §Multi-tenant pour le détail complet, et
`tests/SamaEcole.IntegrationTests` catégorie `MultiTenant` pour le test qui doit
prouver que cette policy bloque même si le Global Query Filter EF Core est un jour
oublié sur une nouvelle table (défense en profondeur, AGENTS.md règle #2).
