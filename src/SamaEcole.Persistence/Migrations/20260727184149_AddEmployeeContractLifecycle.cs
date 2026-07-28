using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket Volume 1 §14.1 — clôture et modification d'un contrat, avec historique. `EndDate` marque
    /// une clôture définitive (plus aucune fiche de paie générable, voir GenerateFichePaieCommandHandler) ;
    /// les index uniques sur TeacherId/UserId sont étendus à "EndDate IS NULL" pour qu'un contrat clôturé
    /// n'empêche plus la reprise de la même personne. `employee_contract_histories` est APPEND-ONLY,
    /// même raisonnement que user_status_history/fee_change_histories : le rôle applicatif ne reçoit que
    /// SELECT/INSERT, jamais UPDATE ni DELETE — un historique de rémunération doit être opposable.
    /// </summary>
    public partial class AddEmployeeContractLifecycle : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts");

            migrationBuilder.AddColumn<DateOnly>(
                name: "EndDate",
                table: "employee_contracts",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "employee_contract_histories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeContractId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChangeType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PreviousBaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    PreviousHourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    PreviousTransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    NewBaseSalary = table.Column<decimal>(type: "numeric", nullable: false),
                    NewHourlyRate = table.Column<decimal>(type: "numeric", nullable: false),
                    NewTransportAllowance = table.Column<decimal>(type: "numeric", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
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
                    table.PrimaryKey("PK_employee_contract_histories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_employee_contract_histories_employee_contracts_EmployeeCont~",
                        column: x => x.EmployeeContractId,
                        principalTable: "employee_contracts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts",
                column: "TeacherId",
                unique: true,
                filter: "\"TeacherId\" IS NOT NULL AND \"EndDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND \"EndDate\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_employee_contract_histories_EmployeeContractId_ChangedAt",
                table: "employee_contract_histories",
                columns: new[] { "EmployeeContractId", "ChangedAt" });

            // --- Isolation multi-tenant (AGENTS.md règle #2) : écrite à la main, comme dans
            // AddFeeInstallmentPlans — EF ne génère jamais ces policies. RlsCoverageTests échoue si
            // elle manque.
            migrationBuilder.Sql("ALTER TABLE employee_contract_histories ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY employee_contract_histories_tenant_isolation ON employee_contract_histories
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- On repart de zéro : les ALTER DEFAULT PRIVILEGES ont pu accorder UPDATE/DELETE.
                        EXECUTE 'REVOKE ALL ON employee_contract_histories FROM {AppRole}';
                        EXECUTE 'GRANT SELECT, INSERT ON employee_contract_histories TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''historique des contrats sera inaccessible à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS employee_contract_histories_tenant_isolation ON employee_contract_histories;");

            migrationBuilder.DropTable(
                name: "employee_contract_histories");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_TeacherId",
                table: "employee_contracts");

            migrationBuilder.DropIndex(
                name: "IX_employee_contracts_UserId",
                table: "employee_contracts");

            migrationBuilder.DropColumn(
                name: "EndDate",
                table: "employee_contracts");

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
        }
    }
}
