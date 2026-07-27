using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiSchoolMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_schools",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_user_schools", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_schools_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_user_schools_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_user_schools_SchoolId",
                table: "user_schools",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_user_schools_UserId_SchoolId_IsDeleted",
                table: "user_schools",
                columns: new[] { "UserId", "SchoolId", "IsDeleted" },
                unique: true);

            // --- Privilèges du rôle applicatif ---
            // Le GRANT global de la migration EnableRowLevelSecurity ne portait que sur les tables
            // EXISTANTES à l'époque : toute table créée depuis doit accorder ses privilèges
            // explicitement, faute de quoi l'application se voit refuser l'accès à l'exécution
            // (« permission denied for table »).
            //
            // `promo_codes` est réparée ICI et non dans sa propre migration : celle-ci a pu être
            // appliquée entre-temps, et AGENTS.md interdit de modifier une migration déjà passée. Un
            // GRANT est idempotent, le rejouer sur une base déjà correcte est sans effet.
            //
            // AUCUNE policy RLS sur ces deux tables, et c'est délibéré : `promo_codes` n'a pas de
            // SchoolId (table plateforme), et `user_schools` doit justement laisser voir les
            // rattachements aux AUTRES écoles — une policy sur SchoolId y masquerait précisément ce
            // que la bascule vient y chercher (voir l'entité UserSchool). Le filtrage repose sur
            // UserId, issu du claim `sub`. Ces deux tables sont donc hors du périmètre de
            // RlsCoverageTests, au même titre que `schools` et `subscriptions`.
            const string appRole = "sama_ecole_app";

            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "user_schools" TO {{appRole}}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "promo_codes" TO {{appRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire user_schools ni promo_codes.', '{{appRole}}';
                    END IF;
                END
                $inner$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_schools");
        }
    }
}
