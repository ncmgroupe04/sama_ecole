using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPayrollAndTax : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "employee_contracts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    TeacherId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
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
                    table.PrimaryKey("PK_employee_contracts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_contracts_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_contracts_teachers_TeacherId",
                        column: x => x.TeacherId,
                        principalTable: "teachers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_employee_contracts_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "taxe_declarations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    TotalIpres = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalCss = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalVrs = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalBrs = table.Column<decimal>(type: "numeric", nullable: false),
                    TvaCollected = table.Column<decimal>(type: "numeric", nullable: false),
                    TvaDeductible = table.Column<decimal>(type: "numeric", nullable: false),
                    NetTva = table.Column<decimal>(type: "numeric", nullable: false),
                    TotalDueToState = table.Column<decimal>(type: "numeric", nullable: false),
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
                    table.PrimaryKey("PK_taxe_declarations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_taxe_declarations_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fiche_paies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    Month = table.Column<int>(type: "integer", nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    HoursWorked = table.Column<decimal>(type: "numeric", nullable: false),
                    GrossSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    TransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    IpresEmployee = table.Column<decimal>(type: "numeric", nullable: false),
                    IpresEmployer = table.Column<decimal>(type: "numeric", nullable: false),
                    CssEmployer = table.Column<decimal>(type: "numeric", nullable: false),
                    Vrs = table.Column<decimal>(type: "numeric", nullable: false),
                    Brs = table.Column<decimal>(type: "numeric", nullable: false),
                    NetSalary = table.Column<decimal>(type: "numeric", nullable: false),
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
                    table.PrimaryKey("PK_fiche_paies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_fiche_paies_employee_contracts_EmployeeContractId",
                        column: x => x.EmployeeContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_fiche_paies_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_SchoolId",
                table: "employee_contracts",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts",
                column: "TeacherId",
                unique: true,
                filter: "\"TeacherId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_EmployeeContractId",
                table: "fiche_paies",
                column: "EmployeeContractId");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId",
                table: "fiche_paies",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_fiche_paies_SchoolId_EmployeeContractId_Month_Year",
                table: "fiche_paies",
                columns: new[] { "SchoolId", "EmployeeContractId", "Month", "Year" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_taxe_declarations_SchoolId",
                table: "taxe_declarations",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_taxe_declarations_SchoolId_Month_Year",
                table: "taxe_declarations",
                columns: new[] { "SchoolId", "Month", "Year" },
                unique: true);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            string[] tenantTables = ["employee_contracts", "fiche_paies", "taxe_declarations"];
            string appRole = "sama_ecole_app";
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                migrationBuilder.Sql($$"""
                    DO $inner$
                    BEGIN
                        IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{appRole}}') THEN
                            EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON "{{table}}" TO {{appRole}}';
                        ELSE
                            RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{table}}.', '{{appRole}}';
                        END IF;
                    END
                    $inner$;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            string[] tenantTables = ["employee_contracts", "fiche_paies", "taxe_declarations"];
            foreach (var table in tenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "fiche_paies");

            migrationBuilder.DropTable(
                name: "taxe_declarations");

            migrationBuilder.DropTable(
                name: "employee_contracts");
        }
    }
}
