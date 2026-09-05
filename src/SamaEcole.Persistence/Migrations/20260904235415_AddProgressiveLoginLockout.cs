using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Remplace le verrouillage à durée FIXE de auth_touch_login (AddAuthentication) par un
    /// verrouillage PROGRESSIF : 1 minute au 5e échec consécutif, puis 1 heure au 8e (+3), et +1 heure
    /// par tranche de 3 échecs supplémentaires (11e -> 2h, 14e -> 3h, ...). Le paramètre
    /// p_lockout_minutes disparaît : la durée n'est plus configurable en entrée, elle est dérivée du
    /// nombre d'échecs — PostgreSQL identifiant une fonction par sa signature, l'ancienne version à 4
    /// arguments doit être supprimée avant de recréer la fonction à 3.
    /// </summary>
    public partial class AddProgressiveLoginLockout : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_touch_login(uuid, boolean, integer, integer);");

            migrationBuilder.Sql("""
                CREATE FUNCTION auth_touch_login(
                    p_user_id uuid,
                    p_success boolean,
                    p_max_failures integer)
                RETURNS void
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE users
                    SET "AccessFailedCount" = CASE WHEN p_success THEN 0 ELSE "AccessFailedCount" + 1 END,
                        "LockoutEndAt" = CASE
                            WHEN p_success THEN NULL
                            -- Palier initial : verrouillage court, le temps de décourager un script
                            -- automatisé sans pénaliser lourdement un directeur qui a mal tapé son mot
                            -- de passe cinq fois de suite.
                            WHEN "AccessFailedCount" + 1 = p_max_failures
                                THEN NOW() + INTERVAL '1 minute'
                            -- Paliers suivants, tous les 3 échecs au-delà du seuil initial (8, 11, 14…) :
                            -- +1h par palier. Entre deux paliers (6e, 7e échec...), le compte reste
                            -- déverrouillé dès l'expiration du verrou précédent — seul le compteur avance.
                            WHEN "AccessFailedCount" + 1 > p_max_failures
                                 AND ("AccessFailedCount" + 1 - p_max_failures) % 3 = 0
                                THEN NOW() + make_interval(hours => ("AccessFailedCount" + 1 - p_max_failures) / 3)
                            ELSE "LockoutEndAt"
                        END,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_user_id;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_touch_login(uuid, boolean, integer) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_touch_login(uuid, boolean, integer) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''authentification ne fonctionnera pas. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_touch_login(uuid, boolean, integer);");

            migrationBuilder.Sql("""
                CREATE FUNCTION auth_touch_login(
                    p_user_id uuid,
                    p_success boolean,
                    p_max_failures integer,
                    p_lockout_minutes integer)
                RETURNS void
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE users
                    SET "AccessFailedCount" = CASE WHEN p_success THEN 0 ELSE "AccessFailedCount" + 1 END,
                        "LockoutEndAt" = CASE
                            WHEN p_success THEN NULL
                            WHEN "AccessFailedCount" + 1 >= p_max_failures
                                THEN NOW() + make_interval(mins => p_lockout_minutes)
                            ELSE "LockoutEndAt"
                        END,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_user_id;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_touch_login(uuid, boolean, integer, integer) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_touch_login(uuid, boolean, integer, integer) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''authentification ne fonctionnera pas. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
