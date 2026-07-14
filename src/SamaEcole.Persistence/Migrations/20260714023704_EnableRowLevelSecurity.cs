using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-A03 — Row-Level Security PostgreSQL (AGENTS.md règle #2).
    ///
    /// Le Global Query Filter EF Core ne protège que le code C# qui passe par EF. La RLS, elle,
    /// protège la BASE : même une requête SQL brute, même un ORM mal configuré, même un filtre
    /// oublié sur une nouvelle entité, ne peuvent lire les données d'une autre école.
    ///
    /// Le tenant courant est lu dans la variable de session `app.current_school_id`, posée à chaque
    /// ouverture de connexion par TenantConnectionInterceptor à partir du claim JWT. Si elle est
    /// absente ou vide, NULLIF la ramène à NULL : la comparaison vaut NULL et AUCUNE ligne ne passe.
    /// La RLS échoue donc en fermeture, jamais en ouverture.
    ///
    /// Table `users` volontairement EXCLUE : l'authentification cherche un utilisateur par e-mail
    /// AVANT de connaître son école, et `SchoolId` y est nullable (comptes plateforme). Une policy
    /// naïve sur SchoolId rendrait le login impossible. Son cloisonnement mérite une policy dédiée —
    /// à traiter séparément plutôt que de l'improviser ici.
    /// </summary>
    public partial class EnableRowLevelSecurity : Migration
    {
        /// <summary>Tables tenant. Toute nouvelle table portant un SchoolId doit être ajoutée ici.</summary>
        private static readonly string[] TenantTables = ["students", "subscriptions", "matricule_sequences"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'INSÉRER une ligne pour une
                //               autre école, ou de déplacer une ligne existante vers une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif. Le rôle lui-même est créé HORS migration
            // (docker/postgres/init pour le dev et la CI, playbook d'exploitation en production) :
            // un rôle est un objet de cluster, pas de base, et le créer ici imposerait d'écrire son
            // mot de passe en clair dans le dépôt.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT USAGE ON SCHEMA public TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO {AppRole}';
                        EXECUTE 'GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA public TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas se connecter. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
                migrationBuilder.Sql($"ALTER TABLE {table} DISABLE ROW LEVEL SECURITY;");
            }

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON ALL TABLES IN SCHEMA public FROM {AppRole}';
                        EXECUTE 'REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM {AppRole}';
                        EXECUTE 'REVOKE USAGE ON SCHEMA public FROM {AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}