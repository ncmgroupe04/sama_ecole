using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Souscription commerciale par école (<c>tenant_subscriptions</c>) : profil, tranche d'effectif,
    /// plafonds, modules. Crée la table, sa RLS, et reprend toutes les écoles existantes en ACTIVE /
    /// illimité. Réversible : Down supprime la table.
    /// </summary>
    public partial class AddTenantSubscriptions : Migration
    {
        private const string Table = "tenant_subscriptions";

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tenant_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProfileType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    StudentQuotaTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaxStudentLimit = table.Column<int>(type: "integer", nullable: false),
                    SoftQuotaLimit = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsPedagogyEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsFinanceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsInternatEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsCoranModuleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_tenant_subscriptions", x => x.Id);
                    table.CheckConstraint("CK_tenant_subscriptions_max_positive", "\"MaxStudentLimit\" > 0");
                    table.CheckConstraint("CK_tenant_subscriptions_soft_gte_max", "\"SoftQuotaLimit\" >= \"MaxStudentLimit\"");
                    table.ForeignKey(
                        name: "FK_tenant_subscriptions_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_tenant_subscriptions_SchoolId",
                table: "tenant_subscriptions",
                column: "SchoolId",
                unique: true,
                filter: "NOT \"IsDeleted\"");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère jamais les policies RLS : elles s'écrivent à la main, et RlsCoverageTests
            // échoue si elles manquent. Aucune « reset_school_data » ici : la souscription est un attribut
            // du COMPTE (profil, tranche), pas une donnée scolaire que « Réinitialiser l'école » purge.
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Aucun DELETE : le soft delete n'émet jamais de SQL DELETE (règle #6).
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

            // --- Reprise des écoles existantes (aucune interruption de service) ---
            // Toute école existante reçoit une souscription ACTIVE à plafond ILLIMITÉ (tranche
            // Tier4_Custom, int.MaxValue = 2147483647) : le quota ne peut rien casser rétroactivement.
            //
            //   * Profil déjà choisi (school_settings."ProfileEtablissement") : conservé, traduit vers
            //     ProfileType — Simplifie→ComptabiliteRapports, ElementairePrimaire→Elementaire,
            //     General→EnseignementGeneral, FrancoArabe→FrancoArabe, DaaraInternat→InternatDaara.
            //   * Aucun profil (NULL, ou pas de ligne de réglages) : EnseignementGeneral.
            //   * Modules : recopiés depuis school_settings pour ne rien changer au comportement actuel ;
            //     sans ligne de réglages, valeurs par défaut des réglages (Pédagogie + Finance actives).
            //
            // Une seule ligne par école : l'index unique UX_tenant_subscriptions_SchoolId le garantit, et
            // school_settings est lui-même unique par école (aucun doublon de jointure possible).
            migrationBuilder.Sql($$"""
                INSERT INTO "{{Table}}" (
                    "Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                    "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled",
                    "CreatedAt", "CreatedBy", "IsDeleted")
                SELECT
                    gen_random_uuid(),
                    s."Id",
                    CASE ss."ProfileEtablissement"
                        WHEN 'Simplifie' THEN 'ComptabiliteRapports'
                        WHEN 'ElementairePrimaire' THEN 'Elementaire'
                        WHEN 'FrancoArabe' THEN 'FrancoArabe'
                        WHEN 'DaaraInternat' THEN 'InternatDaara'
                        ELSE 'EnseignementGeneral'
                    END,
                    'Tier4_Custom',
                    2147483647,
                    2147483647,
                    'Active',
                    COALESCE(ss."IsPedagogyEnabled", true),
                    COALESCE(ss."IsFinanceEnabled", true),
                    COALESCE(ss."IsInternatEnabled", false),
                    COALESCE(ss."IsCoranModuleEnabled", false),
                    now(),
                    'migration:AddTenantSubscriptions',
                    false
                FROM schools s
                LEFT JOIN school_settings ss ON ss."SchoolId" = s."Id" AND NOT ss."IsDeleted";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // La policy et les droits disparaissent avec la table. Les lignes reprises sont perdues :
            // elles se recalculent depuis school_settings en rejouant Up.
            migrationBuilder.DropTable(
                name: "tenant_subscriptions");
        }
    }
}
