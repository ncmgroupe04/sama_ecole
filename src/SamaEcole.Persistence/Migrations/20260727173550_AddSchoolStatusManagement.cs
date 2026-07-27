using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Console Super Admin — activation/suspension/blocage d'un établissement (JGK-B01). `schools`
    /// n'est pas sous RLS, mais `users`/`refresh_tokens` le sont (par la table users) : couper
    /// immédiatement toutes les sessions de l'école visée exige donc la même fonction SECURITY DEFINER
    /// que le reste de la console Super Admin (voir auth_find_active_director_by_school, migration
    /// AddPlatformSubscriptionsAndImpersonation) — sans quoi une suspension resterait sans effet
    /// jusqu'à l'expiration naturelle des refresh tokens (jusqu'à 14 jours).
    /// </summary>
    public partial class AddSchoolStatusManagement : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string OwnerRole = "sama_ecole";
        private const string FunctionSignature = "revoke_refresh_tokens_by_school(uuid)";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION public.revoke_refresh_tokens_by_school(p_school_id uuid)
                RETURNS integer
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    WITH revoked AS (
                        UPDATE refresh_tokens rt
                        SET "RevokedAt" = NOW()
                        FROM users u
                        WHERE rt."UserId" = u."Id"
                          AND u."SchoolId" = p_school_id
                          AND rt."RevokedAt" IS NULL
                        RETURNING rt."Id"
                    )
                    SELECT COUNT(*)::integer FROM revoked;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{OwnerRole}') THEN
                        EXECUTE 'ALTER FUNCTION public.{FunctionSignature} OWNER TO {OwnerRole}';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {FunctionSignature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {FunctionSignature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la suspension d''établissement ne coupera aucune session.', '{AppRole}';
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
