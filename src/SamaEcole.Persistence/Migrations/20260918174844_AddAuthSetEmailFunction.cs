using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Changement d'e-mail self-service (POST /auth/change-email, ChangeUserEmailCommand). `users` est
    /// sous RLS (ticket JGK-A03) : un Super Admin qui change son propre e-mail n'a aucun SchoolId de
    /// session pour satisfaire la policy. Même patron que auth_set_password_hash (migration
    /// AddPasswordResetTokens) : une fonction SECURITY DEFINER dédiée, MINIMALE — elle ne touche que
    /// l'e-mail, rien de plus.
    /// </summary>
    public partial class AddAuthSetEmailFunction : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Aucun RAISE EXCEPTION custom sur le doublon : l'UPDATE laisse l'index unique citext de
            // `users.Email` (UserConfiguration, filtré sur IsDeleted) le refuser NATIVEMENT (SQLSTATE
            // 23505). AuthStore.SetEmailAsync traduit alors l'exception Npgsql avec ConstraintName /
            // TableName renseignés par Postgres lui-même — exactement le flux de
            // SchoolProvisioningStore.CreateInitialDirectorAsync. Un message construit ici, en SQL,
            // n'aurait pas ces deux champs et retomberait sur le message générique du catalogue.
            migrationBuilder.Sql("""
                CREATE FUNCTION auth_set_email(p_user_id uuid, p_new_email text)
                RETURNS void
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE users
                    SET "Email" = p_new_email,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_user_id
                      AND "IsDeleted" = FALSE;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Personne d'autre que le rôle applicatif ne doit pouvoir appeler cette
                        -- fonction : elle contourne la RLS par construction.
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_set_email(uuid, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_set_email(uuid, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le changement d''e-mail ne fonctionnera pas. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_set_email(uuid, text);");
        }
    }
}
