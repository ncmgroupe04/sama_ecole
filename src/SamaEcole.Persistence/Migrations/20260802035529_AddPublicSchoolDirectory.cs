using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Annuaire PUBLIC des établissements (B2C). Ajoute à <c>schools</c> les seules données de vitrine
    /// — consentement, ville/région structurées, présentation — puis expose une VUE en lecture seule
    /// qui matérialise le contrat d'étanchéité côté BASE.
    ///
    /// POURQUOI UNE VUE ET NON UNE POLICY RLS SUR <c>schools</c> :
    /// <c>schools</c> est la RACINE du tenant, pas une table tenant (elle ne porte pas de SchoolId).
    /// Deux usages parfaitement légitimes s'y font SANS <c>app.current_school_id</c> : la console Super
    /// Admin (qui liste toutes les écoles — il n'a aucun claim schoolId par conception) et le chemin de
    /// login (qui résout l'utilisateur avant de connaître son école). Une policy comparant SchoolId au
    /// tenant courant les casserait toutes les deux. La vue, elle, ne retire aucun droit existant : elle
    /// AJOUTE une surface publique volontairement étroite.
    ///
    /// CE QUE LA VUE GARANTIT, indépendamment du code C# qui l'interroge :
    ///   • colonnes — la liste du SELECT est FIGÉE : NINEA, RCCM, statut, champs d'audit et en-têtes
    ///     administratifs (IA/IEF) sont hors de portée, même d'un « SELECT * » ;
    ///   • lignes — le WHERE est FIGÉ : une école sans consentement, suspendue ou archivée n'a aucune
    ///     représentation ici, il n'existe aucun paramètre permettant de l'y faire apparaître.
    ///
    /// CONTOURNEMENT DE RLS, ASSUMÉ ET BORNÉ : la vue appartient au rôle propriétaire (BYPASSRLS), elle
    /// lit donc <c>classrooms</c> hors RLS pour en dériver les cycles proposés. C'est NÉCESSAIRE — un
    /// appel anonyme n'a pas de tenant, la RLS de <c>classrooms</c> lui renverrait zéro ligne et aucun
    /// cycle ne pourrait jamais s'afficher. Le contournement se limite à une agrégation de LIBELLÉS DE
    /// CYCLE (« Primaire », « Lycee »…) pour des écoles ayant explicitement consenti : aucune donnée
    /// d'élève, de note ou de paiement n'est atteignable par ce chemin, et la vue n'accepte aucun
    /// paramètre — il n'y a donc rien à détourner. Même raisonnement que les fonctions SECURITY DEFINER
    /// du chemin de login et de provisionnement d'abonnement.
    /// </summary>
    public partial class AddPublicSchoolDirectory : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "schools",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPubliclyListed",
                table: "schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PublicDescription",
                table: "schools",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "schools",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_schools_public_directory",
                table: "schools",
                columns: new[] { "City", "Region" },
                filter: "\"IsPubliclyListed\" = TRUE AND \"IsDeleted\" = FALSE");

            // Report de la ville/région déjà saisies à l'inscription (school_registration_requests) vers
            // l'école effectivement créée. Ces deux champs étaient collectés puis PERDUS à l'approbation :
            // sans ce rattrapage, toute école existante arriverait dans l'annuaire sans ville, et le
            // filtre par ville — le principal critère de recherche d'un parent — ne renverrait rien.
            // Ne touche QUE les lignes dont la ville est absente : une valeur corrigée à la main par un
            // Directeur ne se fait jamais écraser par la valeur historique de sa candidature.
            migrationBuilder.Sql("""
                UPDATE schools s
                SET "City"   = COALESCE(s."City",   r."City"),
                    "Region" = COALESCE(s."Region", r."Region")
                FROM school_registration_requests r
                WHERE r."CreatedSchoolId" = s."Id"
                  AND (s."City" IS NULL OR s."Region" IS NULL);
                """);

            // Vue d'annuaire — voir la remarque de classe pour le raisonnement de sécurité.
            // CREATE OR REPLACE plutôt que CREATE : la migration doit pouvoir être rejouée sur une base
            // où un Down() partiel aurait laissé la vue en place.
            migrationBuilder.Sql("""
                CREATE OR REPLACE VIEW public_school_directory AS
                SELECT
                    s."Id"                AS "Id",
                    s."Name"              AS "Name",
                    s."City"              AS "City",
                    s."Region"            AS "Region",
                    s."PublicDescription" AS "PublicDescription",
                    s."LogoUrl"           AS "LogoUrl",
                    s."Address"           AS "Address",
                    s."Phone"             AS "Phone",
                    s."Email"             AS "Email",
                    COALESCE(
                        (SELECT array_agg(DISTINCT c."Cycle"::text ORDER BY c."Cycle"::text)
                         FROM classrooms c
                         WHERE c."SchoolId" = s."Id"
                           AND c."IsDeleted" = FALSE),
                        ARRAY[]::text[]
                    )                     AS "Cycles"
                FROM schools s
                WHERE s."IsPubliclyListed" = TRUE
                  AND s."IsDeleted" = FALSE
                  AND s."Status" = 'Active';
                """);

            // Droits : LECTURE SEULE pour le rôle applicatif, rien pour personne d'autre.
            //
            // Le REVOKE explicite sur anon/authenticated est une ceinture de sécurité, pas une
            // correction : l'audit de sécurité a confirmé que ces rôles Supabase n'ont aucun grant sur
            // le schéma public, et que celui-ci a été retiré des « Exposed schemas » du projet — l'API
            // PostgREST ne sert donc rien. Si un jour quelqu'un rouvrait cette exposition, cette ligne
            // empêcherait l'annuaire d'être servi par un chemin qui échappe totalement au backend (et
            // donc au rate limiting et à la journalisation).
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT SELECT ON public_school_directory TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''annuaire public ne pourra pas être lu.', '{AppRole}';
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
                        EXECUTE 'REVOKE ALL ON public_school_directory FROM anon';
                    END IF;

                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
                        EXECUTE 'REVOKE ALL ON public_school_directory FROM authenticated';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La vue AVANT les colonnes : elle en dépend, PostgreSQL refuserait sinon le DROP COLUMN.
            migrationBuilder.Sql("DROP VIEW IF EXISTS public_school_directory;");

            migrationBuilder.DropIndex(
                name: "IX_schools_public_directory",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "City",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "IsPubliclyListed",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "PublicDescription",
                table: "schools");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "schools");
        }
    }
}
