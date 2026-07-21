using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Réinitialisation de mot de passe self-service (POST /auth/forgot-password puis /reset-password).
    ///
    /// AUCUNE policy RLS sur `password_reset_tokens`, et ce n'est pas un oubli : la table ne porte pas
    /// de SchoolId et n'a pas à en porter. Celui qui a oublié son mot de passe n'est PAS authentifié —
    /// il n'y a aucun tenant à résoudre, et une policy sur SchoolId rendrait la réinitialisation
    /// structurellement impossible. Elle ne contient d'ailleurs aucune donnée d'établissement : un
    /// UserId, un condensat opaque, des dates. Exactement le raisonnement, et le précédent, de
    /// `refresh_tokens` (migration AddAuthentication) — voir aussi l'entité PasswordResetToken.
    /// Elle n'est donc PAS ajoutée à TenantTables.
    ///
    /// La table `users`, elle, EST sous RLS : fixer le nouveau mot de passe passe par une fonction
    /// SECURITY DEFINER dédiée, sur le modèle de auth_touch_login.
    /// </summary>
    public partial class AddPasswordResetTokens : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
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
                    table.PrimaryKey("PK_password_reset_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_password_reset_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_TokenHash",
                table: "password_reset_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_password_reset_tokens_UserId",
                table: "password_reset_tokens",
                column: "UserId");

            // Fixe le mot de passe SANS tenant : `users` est sous RLS, une écriture EF depuis ce chemin
            // anonyme ne verrait aucune ligne et l'UPDATE serait un silencieux « 0 ligne modifiée ».
            // Volontairement MINIMALE — elle ne touche que le hash : lui confier plus (statut, rôle)
            // élargirait sans raison une fonction qui contourne la RLS par construction.
            //
            // Le compteur d'échecs et le verrouillage sont remis à zéro : un compte verrouillé par une
            // rafale de tentatives resterait sinon inaccessible à son propriétaire légitime pendant
            // toute la durée du verrou, alors même qu'il vient de prouver la maîtrise de sa boîte mail.
            migrationBuilder.Sql("""
                CREATE FUNCTION auth_set_password_hash(p_user_id uuid, p_password_hash text)
                RETURNS void
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    UPDATE users
                    SET "PasswordHash" = p_password_hash,
                        "AccessFailedCount" = 0,
                        "LockoutEndAt" = NULL,
                        "UpdatedAt" = NOW()
                    WHERE "Id" = p_user_id
                      AND "IsDeleted" = FALSE;
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Nouvelle table : les GRANT des migrations précédentes ne la couvrent pas.
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON password_reset_tokens TO {AppRole}';

                        -- Personne d'autre que le rôle applicatif ne doit pouvoir appeler cette
                        -- fonction : elle contourne la RLS par construction.
                        EXECUTE 'REVOKE ALL ON FUNCTION auth_set_password_hash(uuid, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION auth_set_password_hash(uuid, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la réinitialisation de mot de passe ne fonctionnera pas. Voir docker/postgres/init/.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS auth_set_password_hash(uuid, text);");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");
        }
    }
}
