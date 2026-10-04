using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Module Tenues — trois tables tenant : modèles (uniform_items), tailles (uniform_variants) et dotations
    /// (student_uniforms), au-dessus de l'Inventaire et de Finance.
    ///
    /// Ce que le scaffolding EF n'écrit PAS et qui est ajouté à la main ci-dessous :
    ///   1. Les policies RLS (AGENTS.md règle #2). Une table tenant protégée par le seul filtre EF fuit dès la
    ///      première requête SQL brute — RlsCoverageTests échoue si l'une des trois manque.
    ///   2. Le régime de DROITS : SELECT, INSERT, UPDATE, jamais DELETE (règle #6, tenue par la base).
    ///
    /// Les trois contraintes d'unicité (SchoolId, Id) ajoutées à stock_movements, inventory_items et
    /// enrollment_fee_lines sont les clés cibles des FK COMPOSITES tenant-safe (même technique que
    /// student_subject_exemptions) : une FK s'exécute hors RLS et ne vérifie pas l'école, le couple si.
    /// Elles s'ajoutent à des colonnes déjà uniques (Id est la clé primaire) : aucune donnée existante ne peut les violer.
    ///
    /// Les fonctions de purge sont patchées par la migration suivante, AddUniformsToPurges.
    /// </summary>
    public partial class AddUniformsModule : Migration
    {
        private static readonly string[] TenantTables = ["uniform_items", "uniform_variants", "student_uniforms"];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_stock_movements_SchoolId_Id",
                table: "stock_movements",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_inventory_items_SchoolId_Id",
                table: "inventory_items",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_enrollment_fee_lines_SchoolId_Id",
                table: "enrollment_fee_lines",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateTable(
                name: "uniform_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Gender = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Cycle = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    FeeCategoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityIncluded = table.Column<int>(type: "integer", nullable: false),
                    Notes = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_uniform_items", x => x.Id);
                    table.UniqueConstraint("AK_uniform_items_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.CheckConstraint("CK_uniform_items_quantity_included", "\"QuantityIncluded\" >= 1");
                    table.ForeignKey(
                        name: "FK_uniform_items_fee_categories_SchoolId_FeeCategoryId",
                        columns: x => new { x.SchoolId, x.FeeCategoryId },
                        principalTable: "fee_categories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_uniform_items_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "uniform_variants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    UniformItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    SizeLabel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    InventoryItemId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_uniform_variants", x => x.Id);
                    table.UniqueConstraint("AK_uniform_variants_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_uniform_variants_inventory_items_SchoolId_InventoryItemId",
                        columns: x => new { x.SchoolId, x.InventoryItemId },
                        principalTable: "inventory_items",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_uniform_variants_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_uniform_variants_uniform_items_SchoolId_UniformItemId",
                        columns: x => new { x.SchoolId, x.UniformItemId },
                        principalTable: "uniform_items",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_uniforms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolYearId = table.Column<Guid>(type: "uuid", nullable: false),
                    UniformItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    UniformVariantId = table.Column<Guid>(type: "uuid", nullable: true),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeliveredOn = table.Column<DateOnly>(type: "date", nullable: true),
                    DeliveredByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    DeliveryStockMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReturnStockMovementId = table.Column<Guid>(type: "uuid", nullable: true),
                    BillingStatus = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FeeLineId = table.Column<Guid>(type: "uuid", nullable: true),
                    NotBilledReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ReturnReason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItemLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SizeLabel = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    StudentLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
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
                    table.PrimaryKey("PK_student_uniforms", x => x.Id);
                    table.CheckConstraint("CK_student_uniforms_billed_has_fee_line", "\"BillingStatus\" <> 'Billed' OR \"FeeLineId\" IS NOT NULL");
                    table.CheckConstraint("CK_student_uniforms_delivered_has_movement", "\"Status\" NOT IN ('Delivered', 'Returned') OR (\"DeliveredOn\" IS NOT NULL AND \"DeliveredByUserId\" IS NOT NULL AND \"DeliveryStockMovementId\" IS NOT NULL AND \"UniformVariantId\" IS NOT NULL)");
                    table.CheckConstraint("CK_student_uniforms_not_billed_has_reason", "NOT (\"Status\" IN ('Delivered', 'Returned') AND \"BillingStatus\" = 'NotBilled') OR \"NotBilledReason\" IS NOT NULL");
                    table.CheckConstraint("CK_student_uniforms_quantity", "\"Quantity\" > 0");
                    table.CheckConstraint("CK_student_uniforms_returned_has_movement", "\"Status\" <> 'Returned' OR (\"ReturnStockMovementId\" IS NOT NULL AND \"ReturnReason\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_student_uniforms_enrollment_fee_lines_SchoolId_FeeLineId",
                        columns: x => new { x.SchoolId, x.FeeLineId },
                        principalTable: "enrollment_fee_lines",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_school_years_SchoolId_SchoolYearId",
                        columns: x => new { x.SchoolId, x.SchoolYearId },
                        principalTable: "school_years",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_stock_movements_SchoolId_DeliveryStockMove~",
                        columns: x => new { x.SchoolId, x.DeliveryStockMovementId },
                        principalTable: "stock_movements",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_stock_movements_SchoolId_ReturnStockMoveme~",
                        columns: x => new { x.SchoolId, x.ReturnStockMovementId },
                        principalTable: "stock_movements",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_uniform_items_SchoolId_UniformItemId",
                        columns: x => new { x.SchoolId, x.UniformItemId },
                        principalTable: "uniform_items",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_uniforms_uniform_variants_SchoolId_UniformVariantId",
                        columns: x => new { x.SchoolId, x.UniformVariantId },
                        principalTable: "uniform_variants",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_DeliveryStockMovementId",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "DeliveryStockMovementId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_FeeLineId",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "FeeLineId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_ReturnStockMovementId",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "ReturnStockMovementId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_SchoolYearId_Status",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "SchoolYearId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_StudentId",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_UniformItemId",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "UniformItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_student_uniforms_SchoolId_UniformVariantId_Status",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "UniformVariantId", "Status" });

            migrationBuilder.CreateIndex(
                name: "UX_student_uniforms_active",
                table: "student_uniforms",
                columns: new[] { "SchoolId", "EnrollmentId", "UniformItemId" },
                unique: true,
                filter: "\"Status\" IN ('Requested', 'Delivered') AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_uniform_items_SchoolId_FeeCategoryId",
                table: "uniform_items",
                columns: new[] { "SchoolId", "FeeCategoryId" });

            migrationBuilder.CreateIndex(
                name: "UX_uniform_items_name_gender",
                table: "uniform_items",
                columns: new[] { "SchoolId", "Name", "Gender" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_uniform_variants_inventory_item",
                table: "uniform_variants",
                columns: new[] { "SchoolId", "InventoryItemId" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_uniform_variants_item_size",
                table: "uniform_variants",
                columns: new[] { "SchoolId", "UniformItemId", "SizeLabel" },
                unique: true,
                filter: "\"IsDeleted\" = false");
            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // Aucun DELETE nulle part : le soft delete n'émet jamais de SQL DELETE (règle #6).
                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{table}}" TO {{AppRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{AppRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "student_uniforms");

            migrationBuilder.DropTable(
                name: "uniform_variants");

            migrationBuilder.DropTable(
                name: "uniform_items");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_stock_movements_SchoolId_Id",
                table: "stock_movements");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_inventory_items_SchoolId_Id",
                table: "inventory_items");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_enrollment_fee_lines_SchoolId_Id",
                table: "enrollment_fee_lines");
        }
    }
}
