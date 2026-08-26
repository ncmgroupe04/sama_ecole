using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Module Inventaire — quatre tables tenant : catalogue (inventory_categories, inventory_items),
    /// journal de stock (stock_movements) et prêts de matériel (item_assignments).
    ///
    /// Deux choses que le scaffolding EF n'écrit PAS et qui sont ajoutées à la main ci-dessous :
    ///
    ///   1. Les policies RLS. EF ne les génère jamais (comme dans AddBuildingsAndRooms). Une table
    ///      tenant protégée par le seul filtre EF fuit dès la première requête SQL brute —
    ///      RlsCoverageTests échoue si l'une des quatre manque.
    ///   2. Le régime de DROITS de stock_movements : SELECT et INSERT SEULEMENT, sans UPDATE. C'est
    ///      ce GRANT, et non une convention de code, qui rend le journal réellement append-only —
    ///      même dispositif que fee_change_history dans AddFees. Une erreur de saisie se corrige par
    ///      un mouvement inverse ; la base refuse physiquement la rature.
    /// </summary>
    public partial class AddInventoryModule : Migration
    {
        /// <summary>Les quatre tables tenant du module, dans l'ordre de création.</summary>
        private static readonly string[] TenantTables =
            ["inventory_categories", "inventory_items", "stock_movements", "item_assignments"];

        /// <summary>
        /// Journal append-only : aucun UPDATE accordé, contrairement aux trois autres tables dont le
        /// soft delete et le verrou optimiste en ont besoin.
        /// </summary>
        private const string AppendOnlyTable = "stock_movements";

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_categories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_inventory_categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_inventory_categories_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    CategoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuantityTotal = table.Column<int>(type: "integer", nullable: false),
                    QuantityAvailable = table.Column<int>(type: "integer", nullable: false),
                    Condition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Bon"),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: true),
                    LocationLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UnitPrice = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    IsConsumable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_inventory_items", x => x.Id);
                    table.CheckConstraint("CK_inventory_items_quantities", "\"QuantityTotal\" >= 0 AND \"QuantityAvailable\" >= 0 AND \"QuantityAvailable\" <= \"QuantityTotal\"");
                    table.ForeignKey(
                        name: "FK_inventory_items_inventory_categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "inventory_categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_items_rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_items_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "item_assignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    BeneficiaryType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    BeneficiaryLabel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    AssignedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnedOn = table.Column<DateOnly>(type: "date", nullable: true),
                    ReturnedQuantity = table.Column<int>(type: "integer", nullable: true),
                    ReturnCondition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "character varying(25)", maxLength: 25, nullable: false, defaultValue: "EnCours"),
                    Notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
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
                    table.PrimaryKey("PK_item_assignments", x => x.Id);
                    table.CheckConstraint("CK_item_assignments_beneficiary", "((\"StudentId\" IS NOT NULL)::int + (\"TeacherId\" IS NOT NULL)::int + (\"UserId\" IS NOT NULL)::int) = 1\nAND (\"BeneficiaryType\" <> 'Eleve' OR \"StudentId\" IS NOT NULL)\nAND (\"BeneficiaryType\" <> 'Enseignant' OR \"TeacherId\" IS NOT NULL)\nAND (\"BeneficiaryType\" <> 'Personnel' OR \"UserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_item_assignments_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_item_assignments_inventory_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_assignments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_assignments_students_StudentId",
                        column: x => x.StudentId,
                        principalTable: "students",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_assignments_teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_item_assignments_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_movements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    MovementDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CounterpartyLabel = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    QuantityTotalAfter = table.Column<int>(type: "integer", nullable: false),
                    QuantityAvailableAfter = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_stock_movements", x => x.Id);
                    table.CheckConstraint("CK_stock_movements_quantity_positive", "\"Quantity\" > 0");
                    table.ForeignKey(
                        name: "FK_stock_movements_inventory_items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "inventory_items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_stock_movements_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_categories_SchoolId_Name_IsDeleted",
                table: "inventory_categories",
                columns: new[] { "SchoolId", "Name", "IsDeleted" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_CategoryId",
                table: "inventory_items",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_RoomId",
                table: "inventory_items",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_SchoolId_CategoryId",
                table: "inventory_items",
                columns: new[] { "SchoolId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_SchoolId_Code",
                table: "inventory_items",
                columns: new[] { "SchoolId", "Code" },
                unique: true,
                filter: "\"Code\" IS NOT NULL AND \"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_SchoolId_Name",
                table: "inventory_items",
                columns: new[] { "SchoolId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_items_SchoolId_RoomId",
                table: "inventory_items",
                columns: new[] { "SchoolId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_ItemId",
                table: "item_assignments",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_SchoolId_ItemId",
                table: "item_assignments",
                columns: new[] { "SchoolId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_SchoolId_Status_DueOn",
                table: "item_assignments",
                columns: new[] { "SchoolId", "Status", "DueOn" });

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_SchoolId_StudentId",
                table: "item_assignments",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_SchoolId_TeacherId",
                table: "item_assignments",
                columns: new[] { "SchoolId", "TeacherId" });

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_StudentId",
                table: "item_assignments",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_TeacherId",
                table: "item_assignments",
                column: "TeacherId");

            migrationBuilder.CreateIndex(
                name: "IX_item_assignments_UserId",
                table: "item_assignments",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_AssignmentId",
                table: "stock_movements",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_ItemId",
                table: "stock_movements",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_SchoolId_ItemId_MovementDate",
                table: "stock_movements",
                columns: new[] { "SchoolId", "ItemId", "MovementDate" });

            migrationBuilder.CreateIndex(
                name: "IX_stock_movements_SchoolId_MovementDate",
                table: "stock_movements",
                columns: new[] { "SchoolId", "MovementDate" });

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
                // Aucun UPDATE sur le journal : voir AppendOnlyTable.
                var privileges = table == AppendOnlyTable ? "SELECT, INSERT" : "SELECT, INSERT, UPDATE";

                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                            EXECUTE 'GRANT {{privileges}} ON "{{table}}" TO {{AppRole}}';
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
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "item_assignments");

            migrationBuilder.DropTable(
                name: "stock_movements");

            migrationBuilder.DropTable(
                name: "inventory_items");

            migrationBuilder.DropTable(
                name: "inventory_categories");
        }
    }
}
