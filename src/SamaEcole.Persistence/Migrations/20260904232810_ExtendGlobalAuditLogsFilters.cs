using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Console Super Admin — écran Sécurité &amp; Logs (/admin/securite) : le journal ne pouvait être
    /// filtré que par page, contrairement à son équivalent tenant (GetAuditLogsQuery, module/succès).
    /// Étend `get_global_audit_logs` (migration AddPlatformAdminViews) avec 5 paramètres optionnels
    /// (module, succès/échec, école, plage de dates) — NULL désactive chaque condition, même convention
    /// que GetAuditLogsQueryHandler côté tenant. Une fonction SQL ne peut pas gagner de paramètres par
    /// ALTER : DROP + CREATE, comme documenté sur AddPlatformAdminViews.
    /// </summary>
    public partial class ExtendGlobalAuditLogsFilters : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string OwnerRole = "sama_ecole";
        private const string OldSignature = "get_global_audit_logs(int, int)";
        private const string NewSignature = "get_global_audit_logs(int, int, text, boolean, uuid, timestamptz, timestamptz)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {OldSignature};");

            migrationBuilder.Sql("""
                CREATE FUNCTION public.get_global_audit_logs(
                    limit_val int,
                    offset_val int,
                    module_filter text DEFAULT NULL,
                    success_filter boolean DEFAULT NULL,
                    school_id_filter uuid DEFAULT NULL,
                    date_from timestamptz DEFAULT NULL,
                    date_to timestamptz DEFAULT NULL)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "SchoolName" text,
                    "UserId" uuid,
                    "ActorFullName" text,
                    "Module" text,
                    "Action" text,
                    "Success" boolean,
                    "FailureReason" text,
                    "IpAddress" text,
                    "OccurredAt" timestamptz,
                    "TotalCount" integer)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    -- COUNT(*) OVER() : total AVANT LIMIT/OFFSET (donc du FILTRE, pas de la page), porté
                    -- sur chaque ligne — évite un second aller-retour dédié au total pour la pagination.
                    SELECT
                        a."Id", a."SchoolId", s."Name", a."UserId", u."FullName",
                        a."Module", a."Action", a."Success", a."FailureReason", a."IpAddress", a."OccurredAt",
                        COUNT(*) OVER()::integer AS "TotalCount"
                    FROM audit_logs a
                    JOIN schools s ON s."Id" = a."SchoolId"
                    JOIN users u ON u."Id" = a."UserId"
                    WHERE NOT a."IsDeleted"
                      AND (module_filter IS NULL OR a."Module" = module_filter)
                      AND (success_filter IS NULL OR a."Success" = success_filter)
                      AND (school_id_filter IS NULL OR a."SchoolId" = school_id_filter)
                      AND (date_from IS NULL OR a."OccurredAt" >= date_from)
                      AND (date_to IS NULL OR a."OccurredAt" <= date_to)
                    ORDER BY a."OccurredAt" DESC
                    LIMIT limit_val OFFSET offset_val;
                $$;
                """);

            AlterOwnerIfRoleExists(migrationBuilder, "FUNCTION", $"public.{NewSignature}");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {NewSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {NewSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le journal d''activité plateforme du Super Admin sera inaccessible.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {NewSignature};");

            migrationBuilder.Sql("""
                CREATE FUNCTION public.get_global_audit_logs(limit_val int, offset_val int)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "SchoolName" text,
                    "UserId" uuid,
                    "ActorFullName" text,
                    "Module" text,
                    "Action" text,
                    "Success" boolean,
                    "FailureReason" text,
                    "IpAddress" text,
                    "OccurredAt" timestamptz,
                    "TotalCount" integer)
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT
                        a."Id", a."SchoolId", s."Name", a."UserId", u."FullName",
                        a."Module", a."Action", a."Success", a."FailureReason", a."IpAddress", a."OccurredAt",
                        COUNT(*) OVER()::integer AS "TotalCount"
                    FROM audit_logs a
                    JOIN schools s ON s."Id" = a."SchoolId"
                    JOIN users u ON u."Id" = a."UserId"
                    WHERE NOT a."IsDeleted"
                    ORDER BY a."OccurredAt" DESC
                    LIMIT limit_val OFFSET offset_val;
                $$;
                """);

            AlterOwnerIfRoleExists(migrationBuilder, "FUNCTION", $"public.{OldSignature}");

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {OldSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {OldSignature} TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        private static void AlterOwnerIfRoleExists(MigrationBuilder migrationBuilder, string objectKind, string objectName)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                        EXECUTE 'ALTER {objectKind} {objectName} OWNER TO {OwnerRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
