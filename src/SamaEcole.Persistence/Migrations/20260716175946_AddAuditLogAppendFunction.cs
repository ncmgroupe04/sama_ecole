using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-H01 (suite) — capture des connexions (JGK-A04) et des actions Super Admin dans le
    /// journal d'audit, jusqu'ici hors scope d'AuditLoggingBehavior.
    ///
    /// LE PROBLÈME : audit_logs est sous policy RLS comme toute table tenant (règle #2). Un acteur SANS
    /// SchoolId propre — le login avant authentification (aucun tenant établi), ou le Super Admin (qui
    /// n'appartient à aucune école) — voit sa session ne satisfaire le WITH CHECK d'AUCUNE ligne : son
    /// INSERT est rejeté, quelle que soit la valeur de SchoolId visée.
    ///
    /// LA SOLUTION, même schéma que provision_school_director (AddSchoolProvisioning, JGK-B01) : une
    /// fonction SECURITY DEFINER qui contourne la RLS. Elle ne fait qu'ajouter une ligne d'audit —
    /// aucune capacité privilégiée n'en découle (contrairement à créer un compte), la contrainte FK
    /// SchoolId -> schools suffit à empêcher une valeur invalide.
    /// </summary>
    public partial class AddAuditLogAppendFunction : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string FunctionSignature = "append_audit_log(uuid, uuid, text, text, boolean, text, text, timestamptz)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION append_audit_log(
                    p_school_id uuid,
                    p_user_id uuid,
                    p_module text,
                    p_action text,
                    p_success boolean,
                    p_failure_reason text,
                    p_ip_address text,
                    p_occurred_at timestamptz)
                RETURNS void
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    INSERT INTO audit_logs (
                        "Id", "SchoolId", "UserId", "Module", "Action", "Success", "FailureReason",
                        "IpAddress", "OccurredAt", "CreatedAt", "IsDeleted")
                    VALUES (
                        gen_random_uuid(), p_school_id, p_user_id, p_module, p_action, p_success,
                        p_failure_reason, p_ip_address, p_occurred_at, NOW(), FALSE);
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : on le retire à
                        -- PUBLIC avant de l'accorder nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION {FunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {FunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : les connexions et actions Super Admin ne seront pas journalisées.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {FunctionSignature};");
        }
    }
}
