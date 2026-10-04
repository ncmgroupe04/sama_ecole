using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Règle #6 d'AGENTS.md tenue par la BASE : le rôle applicatif ne peut physiquement supprimer AUCUNE ligne
    /// métier (conception soft delete 2026-10-01 §3.1/§3.3). L'audit a montré des DELETE résiduels hérités du
    /// premier <c>GRANT ... ON ALL TABLES</c> et des <c>ALTER DEFAULT PRIVILEGES</c> de l'environnement, dont des
    /// registres financiers (<c>fiche_paies</c>, <c>taxe_declarations</c>, <c>Disbursements</c>).
    ///
    /// L'historique des migrations (<c>__EFMigrationsHistory</c>) est inclus : les migrations s'exécutent sous le
    /// rôle propriétaire, l'application n'a aucune raison de pouvoir en effacer une ligne.
    ///
    /// Les purges légitimes (« remise à neuf » d'une école, suppression d'année en mode test) passent par des
    /// fonctions <c>SECURITY DEFINER</c> exécutées sous le rôle propriétaire : elles n'ont besoin d'aucun droit
    /// du rôle applicatif.
    ///
    /// Révocation DYNAMIQUE sur toutes les tables de <c>public</c> sauf la liste blanche ci-dessous : elle couvre
    /// aussi des tables que seuls les privilèges par défaut d'un environnement auraient rendues supprimables.
    /// La liste blanche ne contient que des tables TECHNIQUES, sans parcours de corbeille ni valeur comptable.
    /// Un test d'intégration échoue si une table hors liste blanche redevient supprimable.
    ///
    /// Retour arrière : correctif en avant privilégié. Le <c>Down</c> ne ré-accorde volontairement rien, pour ne
    /// pas rétablir un droit de suppression physique sur des registres financiers.
    /// </summary>
    public partial class RevokeDeleteOnBusinessTables : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <summary>Tables techniques où le rôle applicatif garde DELETE (jetons éphémères, compteur de séquence).</summary>
        public static readonly string[] TechnicalTablesKeepingDelete =
            ["refresh_tokens", "password_reset_tokens", "matricule_sequences"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var keep = string.Join(", ", TechnicalTablesKeepingDelete.Select(t => $"'{t}'"));

            migrationBuilder.Sql($$"""
                DO $$
                DECLARE t record;
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        RAISE WARNING 'Rôle % absent : aucun privilège à révoquer.', '{{AppRole}}';
                        RETURN;
                    END IF;

                    FOR t IN
                        SELECT c.relname
                        FROM pg_class c
                        JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public'
                          AND c.relkind IN ('r', 'p')
                          AND c.relname NOT IN ({{keep}})
                    LOOP
                        EXECUTE format('REVOKE DELETE ON public.%I FROM {{AppRole}}', t.relname);
                    END LOOP;

                    -- Les tables créées PLUS TARD par ce rôle propriétaire ne doivent plus hériter de DELETE.
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE DELETE ON TABLES FROM {{AppRole}};
                END
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement vide : ne jamais rétablir un droit de suppression physique (roll-forward).
        }
    }
}
