using System.Linq;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Revue finale de la tâche 2 (plan soft delete 2026-10-01) sur <c>RevokeDeleteOnBusinessTables</c> —
    /// trois trous laissés par cette migration (déjà APPLIQUÉE, donc non modifiée : AGENTS.md) :
    ///
    ///   1. La boucle dynamique ne parcourait que <c>relkind IN ('r', 'p')</c> (tables ordinaires et
    ///      partitionnées). <c>public_school_directory</c> (migration <c>AddPublicSchoolDirectory</c>) est
    ///      une VUE à une seule table sur <c>schools</c>, que PostgreSQL traite comme automatiquement
    ///      « updatable » : un <c>DELETE</c> à travers elle s'exécute avec les privilèges du PROPRIÉTAIRE
    ///      de la vue sur <c>schools</c>. Si un environnement avait hérité un DELETE sur cette vue (par les
    ///      mêmes <c>ALTER DEFAULT PRIVILEGES</c> / <c>GRANT ... ON ALL TABLES</c> dont le contexte de
    ///      <c>RevokeDeleteOnBusinessTables</c> parle déjà), la boucle d'origine ne le balayait jamais — la
    ///      révocation aurait manqué sa propre raison d'être pour cette table-racine. On élargit à
    ///      <c>('r', 'p', 'v', 'm', 'f')</c> : tables, tables partitionnées, vues, vues matérialisées, tables
    ///      étrangères. (TRUNCATE n'a de sens réel que sur 'r'/'p'/'m', mais PostgreSQL accepte sans erreur
    ///      un REVOKE TRUNCATE sur une vue ou une table étrangère : rien à protéger spécifiquement ici.)
    ///
    ///   2. Seul <c>DELETE</c> était révoqué. <c>TRUNCATE</c> est un privilège DISTINCT qui vide la table
    ///      aussi efficacement qu'un DELETE non filtré ET ignore la RLS (TRUNCATE n'évalue aucune policy).
    ///      La justification de <c>RevokeDeleteOnBusinessTables</c> (« fermer tout DELETE résiduel ») reste
    ///      incomplète tant que ce second chemin de purge massive reste ouvert.
    ///
    ///   3. Le rôle applicatif gardait <c>UPDATE</c> sur <c>Disbursements</c> alors qu'aucun chemin de code
    ///      ne modifie jamais un décaissement (DeleteDisbursementCommandHandler fait une CONTRE-ÉCRITURE —
    ///      une nouvelle ligne négative — jamais un UPDATE de la ligne d'origine : voir
    ///      ImmutableRegistersTests.Cancelling_A_Disbursement_Adds_A_Negative_Reversal_And_Never_Touches_The_Original).
    ///      L'immutabilité de ce registre ne reposait donc que sur une convention de code, pas sur la BASE.
    ///      On révoque UPDATE SEULEMENT sur <c>Disbursements</c> : Payments et les autres registres gardent
    ///      UPDATE, dont ils ont légitimement besoin pour leurs changements de statut.
    ///
    /// Roll-forward privilégié (même choix que <c>RevokeDeleteOnBusinessTables</c>) : le <c>Down()</c> ne
    /// ré-accorde volontairement rien.
    /// </summary>
    public partial class WidenBusinessRevokeToViewsAndTruncate : Migration
    {
        private const string AppRole = "sama_ecole_app";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var keep = string.Join(", ", RevokeDeleteOnBusinessTables.TechnicalTablesKeepingDelete.Select(t => $"'{t}'"));

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
                          AND c.relkind IN ('r', 'p', 'v', 'm', 'f')
                          AND c.relname NOT IN ({{keep}})
                    LOOP
                        EXECUTE format('REVOKE DELETE, TRUNCATE ON public.%I FROM {{AppRole}}', t.relname);
                    END LOOP;

                    -- Même filet que RevokeDeleteOnBusinessTables, étendu à TRUNCATE : les tables créées
                    -- PLUS TARD par ce rôle propriétaire ne doivent hériter ni de DELETE ni de TRUNCATE.
                    ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE DELETE, TRUNCATE ON TABLES FROM {{AppRole}};

                    -- Disbursements seulement (pas Payments ni les autres registres, qui ont légitimement
                    -- besoin d'UPDATE pour leurs changements de statut) : aucun chemin de code ne modifie
                    -- jamais un décaissement, la correction passe par une contre-écriture (nouvelle ligne).
                    EXECUTE format('REVOKE UPDATE ON public.%I FROM {{AppRole}}', 'Disbursements');
                END
                $$;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Volontairement vide : ne jamais rétablir TRUNCATE, DELETE ou UPDATE sur ces objets (roll-forward).
        }
    }
}
