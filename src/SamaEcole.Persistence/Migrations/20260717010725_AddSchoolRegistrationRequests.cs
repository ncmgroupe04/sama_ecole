using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-I01 — demandes d'inscription self-service (docs/Volume_1_Cahier_des_Charges.md §11.5,
    /// docs/Volume_3_DDS.md §5.7).
    ///
    /// Table PLATEFORME, VOLONTAIREMENT HORS RLS : contrairement aux tables tenant (students, audit_logs…),
    /// elle PRÉCÈDE l'existence de l'école et n'a pas de SchoolId. Elle est écrite par le formulaire public
    /// ANONYME, dont la session n'a aucun tenant — une policy RLS par SchoolId lui fermerait donc la porte.
    /// Elle rejoint schools/subscriptions parmi les entités hors périmètre RLS (docs/Volume_3_DDS.md §2.3).
    ///
    /// GRANT du rôle applicatif posé explicitement (comme AddAuditLogs/AddFees) : les ALTER DEFAULT
    /// PRIVILEGES de docker/postgres/init couvrent le dev, mais PAS l'environnement de test (AuthApiFactory
    /// crée le rôle sans eux). SELECT + INSERT + UPDATE, jamais DELETE : soft delete uniquement (règle #6),
    /// et UPDATE sert à la revue Super Admin (approbation/rejet, ticket JGK-I03).
    /// </summary>
    public partial class AddSchoolRegistrationRequests : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_registration_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TrackingReference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DirectorFullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DirectorEmail = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    DirectorPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DirectorPasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    SchoolName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SchoolAddress = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Region = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EstimatedStudentCount = table.Column<int>(type: "integer", nullable: true),
                    RequestedPlan = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "text", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedSchoolId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_school_registration_requests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_school_registration_requests_TrackingReference",
                table: "school_registration_requests",
                column: "TrackingReference",
                unique: true);

            // Aucune policy RLS ici (table plateforme, voir résumé de classe). On ne pose QUE le GRANT.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Modifiable (revue Super Admin, JGK-I03) mais jamais supprimable physiquement
                        -- (soft delete, règle #6) : SELECT, INSERT, UPDATE, pas DELETE.
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON school_registration_requests TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : les demandes d''inscription seront inaccessibles à l''application.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "school_registration_requests");
        }
    }
}
