using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-F01 — paramétrage des frais : `fee_categories`, `class_fees`, `fee_change_history`.
    ///
    /// Ce qu'EF Core ne génère PAS et qu'il faut poser à la main :
    ///
    ///   1. La policy RLS des TROIS tables tenant (AGENTS.md règle #2), ajoutées à la protection déjà
    ///      en place pour students, classrooms, subjects, school_years, etc.
    ///
    ///   2. Les GRANT du rôle applicatif, à la maille de chaque table :
    ///        - fee_categories et class_fees : SELECT, INSERT, UPDATE (pas DELETE — soft delete, règle #6) ;
    ///        - fee_change_history : SELECT, INSERT SEULEMENT. C'est un journal APPEND-ONLY
    ///          (Volume 1 §7.4). On RÉVOQUE d'abord tout — les ALTER DEFAULT PRIVILEGES de
    ///          docker/postgres/init accordent UPDATE/DELETE sur toute table future — puis on ne
    ///          redonne que SELECT et INSERT. Même un bug ou une injection ne peut alors réécrire un
    ///          barème historisé, exactement comme pour user_status_history (JGK-A05).
    ///
    /// La colonne `xmin` de class_fees est le jeton de verrou optimiste (règle #5) : c'est la colonne
    /// SYSTÈME de PostgreSQL, présente dans toute table. Npgsql la reconnaît et n'émet aucun DDL pour
    /// elle malgré sa présence dans le modèle de migration.
    /// </summary>
    public partial class AddFees : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fee_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    IsRecurring = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_fee_categories", x => x.Id);
                    table.UniqueConstraint("AK_fee_categories_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_fee_categories_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_fees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassroomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
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
                    table.PrimaryKey("PK_class_fees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_fees_classrooms_SchoolId_ClassroomId",
                        columns: x => new { x.SchoolId, x.ClassroomId },
                        principalTable: "classrooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_fees_fee_categories_SchoolId_FeeCategoryId",
                        columns: x => new { x.SchoolId, x.FeeCategoryId },
                        principalTable: "fee_categories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_fees_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_change_history",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassFeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    OldAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    NewAmount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
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
                    table.PrimaryKey("PK_fee_change_history", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fee_change_history_class_fees_ClassFeeId",
                        column: x => x.ClassFeeId,
                        principalTable: "class_fees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_ClassroomId",
                table: "class_fees",
                columns: new[] { "SchoolId", "ClassroomId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_fees_SchoolId_FeeCategoryId_ClassroomId_IsDeleted",
                table: "class_fees",
                columns: new[] { "SchoolId", "FeeCategoryId", "ClassroomId", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_categories_SchoolId_Name_IsDeleted",
                table: "fee_categories",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_fee_change_history_ClassFeeId_ChangedAt",
                table: "fee_change_history",
                columns: new[] { "ClassFeeId", "ChangedAt" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) : les TROIS tables ---
            foreach (var table in new[] { "fee_categories", "class_fees", "fee_change_history" })
            {
                migrationBuilder.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");

                // USING      -> lignes visibles en lecture (SELECT, et cibles d'UPDATE/DELETE).
                // WITH CHECK -> lignes autorisées en écriture : interdit d'écrire pour une autre école.
                migrationBuilder.Sql($"""
                    CREATE POLICY {table}_tenant_isolation ON {table}
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);
            }

            // Droits du rôle applicatif. Le GRANT ON ALL TABLES d'une migration antérieure ne couvrait
            // pas des tables qui n'existaient pas encore.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Barème : modifiable, mais jamais supprimable physiquement (soft delete, règle #6).
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON fee_categories TO {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON class_fees TO {AppRole}';

                        -- Journal APPEND-ONLY : on repart de zéro (les ALTER DEFAULT PRIVILEGES ont pu
                        -- accorder UPDATE/DELETE) puis on ne redonne que SELECT et INSERT.
                        EXECUTE 'REVOKE ALL ON fee_change_history FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON fee_change_history TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas accéder aux tables de frais.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in new[] { "fee_change_history", "class_fees", "fee_categories" })
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON {table};");
            }

            migrationBuilder.DropTable(
                name: "fee_change_history");

            migrationBuilder.DropTable(
                name: "class_fees");

            migrationBuilder.DropTable(
                name: "fee_categories");
        }
    }
}
