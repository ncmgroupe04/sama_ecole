using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-A04 — authentification, et fermeture du dernier trou d'isolation laissé par JGK-A03.
    ///
    /// La table `users` était restée SANS policy RLS, car le login cherche un compte par e-mail AVANT
    /// de connaître son école : sous RLS, une requête sans tenant ne voit aucune ligne, et l'on ne
    /// pourrait donc plus se connecter. La solution n'est pas de laisser la table ouverte, mais de
    /// réserver ce contournement au SEUL chemin d'authentification :
    ///
    ///   * `users` passe sous RLS comme les autres tables tenant ;
    ///   * trois fonctions SECURITY DEFINER (donc exécutées avec les droits du PROPRIÉTAIRE, qui est
    ///     exempté de RLS) exposent exactement les opérations nécessaires au login/refresh, et rien
    ///     de plus : chercher un compte par e-mail, par id, et enregistrer le résultat d'une tentative.
    ///   * le rôle applicatif reçoit EXECUTE sur ces trois fonctions ; EXECUTE est retiré à PUBLIC.
    ///
    /// La surface de contournement se limite ainsi à « lire un compte » et « incrémenter un compteur
    /// d'échecs », au lieu d'un accès libre à toute la table users de toutes les écoles.
    ///
    /// `search_path` est figé sur public dans chaque fonction : sans cela, un rôle pouvant créer un
    /// schéma pourrait y placer une fausse table `users` et détourner une fonction SECURITY DEFINER.
    ///
    /// Conséquence assumée : un Super Admin a `SchoolId` NULL, ses lignes ne sont donc visibles par
    /// AUCUNE session tenant. Ses écrans passeront par des requêtes dédiées (module B), pas par la
    /// lecture directe de `users`.
    /// </summary>
    public partial class AddAuthentication : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AccessFailedCount",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockoutEndAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId",
                table: "refresh_tokens",
                column: "UserId");

            // --- RLS sur users (dette de JGK-A03, cf. en-tête) ---
            migrationBuilder.Sql("ALTER TABLE users ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY users_tenant_isolation ON users
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // --- Chemin d'authentification : les 3 seules opérations autorisées à voir users hors tenant ---
            migrationBuilder.Sql("""
                CREATE FUNCTION auth_find_user_by_email(p_email text)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "Email" text,
                    "PasswordHash" text,
                    "FullName" text,
                    "Role" text,
                    "Status" text,
                    "AccessFailedCount" integer,
                    "LockoutEndAt" timestamptz
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT u."Id", u."SchoolId", u."Email"::text, u."PasswordHash"::text, u."FullName"::text,
                           u."Role"::text, u."Status"::text, u."AccessFailedCount", u."LockoutEndAt"
                    FROM users u
                    WHERE lower(u."Email") = lower(p_email)
                      AND u."IsDeleted" = FALSE;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION auth_find_user_by_id(p_user_id uuid)
                RETURNS TABLE (
                    "Id" uuid,
                    "SchoolId" uuid,
                    "Email" text,
                    "PasswordHash" text,
                    "FullName" text,
                    "Role" text,
                    "Status" text,
                    "AccessFailedCount" integer,
                    "LockoutEndAt" timestamptz
                )
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT u."Id", u."SchoolId", u."Email"::text, u."PasswordHash"::text, u."FullName"::text,
                           u."Role"::text, u."Status"::text, u."AccessFailedCount", u."LockoutEndAt"
                    FROM users u
                    WHERE u."Id" = p_user_id
                      AND u."IsDeleted" = FALSE;
                $$;
                """);

            // Verrouillage après N échecs consécutifs (docs/Volume_7_Security.md §2). Le calcul est fait
            // en base, en une seule instruction : deux tentatives simultanées ne peuvent pas se marcher
            // dessus et « perdre » un échec en chemin.
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
                        -- Nouvelle table : le GRANT ON ALL TABLES de la migration précédente ne la couvrait pas.
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON refresh_tokens TO {AppRole}';

                        -- Personne d'autre que le rôle applicatif ne doit pouvoir appeler ces fonctions :
                        -- elles contournent la RLS par construction.
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_find_user_by_email(text) FROM PUBLIC';
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_find_user_by_id(uuid) FROM PUBLIC';
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_touch_login(uuid, boolean, integer, integer) FROM PUBLIC';

                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_find_user_by_email(text) TO {AppRole}';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_find_user_by_id(uuid) TO {AppRole}';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_touch_login(uuid, boolean, integer, integer) TO {AppRole}';
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
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_touch_login(uuid, boolean, integer, integer);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_find_user_by_id(uuid);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_find_user_by_email(text);");

            migrationBuilder.Sql("DROP POLICY IF EXISTS users_tenant_isolation ON users;");
            migrationBuilder.Sql("ALTER TABLE users DISABLE ROW LEVEL SECURITY;");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropColumn(
                name: "AccessFailedCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutEndAt",
                table: "users");
        }
    }
}
