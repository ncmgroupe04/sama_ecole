using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Modèle Pavillon/Lit de l'Internat (spec docs/superpowers/specs/2026-10-06-internat-backend-and-profile-isolation-design.md
    /// §3) : <c>dormitories</c>, <c>dormitory_rooms</c>, <c>beds</c>, <c>boarding_enrollments</c>, <c>boarding_leaves</c>,
    /// <c>boarding_attendances</c>. EF ne génère jamais les policies RLS : ajoutées à la main ci-dessous (AGENTS.md
    /// règle #2), comme pour <c>AddStudentSubjectExemptions</c>. Les fonctions de purge sont patchées par la migration
    /// suivante, <c>AddBoardingToPurges</c>. Les colonnes héritées <c>enrollments.BoardingStatus/RoomId</c> ne sont PAS
    /// touchées (leur suppression est un lot ultérieur).
    /// </summary>
    public partial class AddBoardingDormitoryModel : Migration
    {
        private static readonly string[] TenantTables =
        [
            "dormitories", "dormitory_rooms", "beds", "boarding_enrollments", "boarding_leaves", "boarding_attendances"
        ];

        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dormitories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Gender = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    SupervisorName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    SupervisorPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    SupervisorUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_dormitories", x => x.Id);
                    table.UniqueConstraint("AK_dormitories_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_dormitories_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dormitories_users_SupervisorUserId",
                        column: x => x.SupervisorUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dormitory_rooms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    DormitoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
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
                    table.PrimaryKey("PK_dormitory_rooms", x => x.Id);
                    table.UniqueConstraint("AK_dormitory_rooms_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_dormitory_rooms_dormitories_SchoolId_DormitoryId",
                        columns: x => new { x.SchoolId, x.DormitoryId },
                        principalTable: "dormitories",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_dormitory_rooms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "beds",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    DormitoryRoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    BedNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Available"),
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
                    table.PrimaryKey("PK_beds", x => x.Id);
                    table.UniqueConstraint("AK_beds_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.CheckConstraint("CK_beds_number_positive", "\"BedNumber\" >= 1");
                    table.CheckConstraint("CK_beds_stored_status", "\"Status\" IN ('Available','Maintenance')");
                    table.ForeignKey(
                        name: "FK_beds_dormitory_rooms_SchoolId_DormitoryRoomId",
                        columns: x => new { x.SchoolId, x.DormitoryRoomId },
                        principalTable: "dormitory_rooms",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_beds_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "boarding_enrollments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    EnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Regime = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BedId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    MedicalNotes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EmergencyContactName = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true),
                    EmergencyContactPhone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true),
                    AllowedExitPersons = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_boarding_enrollments", x => x.Id);
                    table.UniqueConstraint("AK_boarding_enrollments_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.CheckConstraint("CK_boarding_enrollments_dates", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
                    table.CheckConstraint("CK_boarding_enrollments_ended_has_end", "\"IsActive\" OR \"EndDate\" IS NOT NULL");
                    table.CheckConstraint("CK_boarding_enrollments_half_board_no_bed", "\"Regime\" <> 'DemiPensionnaire' OR \"BedId\" IS NULL");
                    table.CheckConstraint("CK_boarding_enrollments_inactive_no_bed", "\"IsActive\" OR \"BedId\" IS NULL");
                    table.ForeignKey(
                        name: "FK_boarding_enrollments_beds_SchoolId_BedId",
                        columns: x => new { x.SchoolId, x.BedId },
                        principalTable: "beds",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boarding_enrollments_enrollments_SchoolId_EnrollmentId",
                        columns: x => new { x.SchoolId, x.EnrollmentId },
                        principalTable: "enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boarding_enrollments_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boarding_enrollments_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "boarding_attendances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardingEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    IsPresent = table.Column<bool>(type: "boolean", nullable: false),
                    Note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
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
                    table.PrimaryKey("PK_boarding_attendances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_boarding_attendances_boarding_enrollments_SchoolId_Boarding~",
                        columns: x => new { x.SchoolId, x.BoardingEnrollmentId },
                        principalTable: "boarding_enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boarding_attendances_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "boarding_leaves",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    BoardingEnrollmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ExpectedReturnDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ActualReturnDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ReasonDetail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    AccompaniedBy = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    IsCompanionUnlisted = table.Column<bool>(type: "boolean", nullable: false),
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
                    table.PrimaryKey("PK_boarding_leaves", x => x.Id);
                    table.CheckConstraint("CK_boarding_leaves_actual", "\"ActualReturnDate\" IS NULL OR \"ActualReturnDate\" >= \"LeaveDate\"");
                    table.CheckConstraint("CK_boarding_leaves_expected", "\"ExpectedReturnDate\" >= \"LeaveDate\"");
                    table.ForeignKey(
                        name: "FK_boarding_leaves_boarding_enrollments_SchoolId_BoardingEnrol~",
                        columns: x => new { x.SchoolId, x.BoardingEnrollmentId },
                        principalTable: "boarding_enrollments",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_boarding_leaves_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_beds_number",
                table: "beds",
                columns: new[] { "SchoolId", "DormitoryRoomId", "BedNumber" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_boarding_attendances_SchoolId_BoardingEnrollmentId",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "BoardingEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_boarding_attendances_SchoolId_Date",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "Date" });

            migrationBuilder.CreateIndex(
                name: "UX_boarding_attendances_night",
                table: "boarding_attendances",
                columns: new[] { "BoardingEnrollmentId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "IX_boarding_enrollments_SchoolId_BedId",
                table: "boarding_enrollments",
                columns: new[] { "SchoolId", "BedId" });

            migrationBuilder.CreateIndex(
                name: "IX_boarding_enrollments_SchoolId_StudentId",
                table: "boarding_enrollments",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "UX_boarding_enrollments_active_bed",
                table: "boarding_enrollments",
                column: "BedId",
                unique: true,
                filter: "\"IsActive\" AND NOT \"IsDeleted\" AND \"BedId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_boarding_enrollments_active_enrollment",
                table: "boarding_enrollments",
                columns: new[] { "SchoolId", "EnrollmentId" },
                unique: true,
                filter: "\"IsActive\" AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_boarding_leaves_SchoolId_BoardingEnrollmentId",
                table: "boarding_leaves",
                columns: new[] { "SchoolId", "BoardingEnrollmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_boarding_leaves_SchoolId_LeaveDate",
                table: "boarding_leaves",
                columns: new[] { "SchoolId", "LeaveDate" });

            migrationBuilder.CreateIndex(
                name: "UX_boarding_leaves_open",
                table: "boarding_leaves",
                column: "BoardingEnrollmentId",
                unique: true,
                filter: "\"ActualReturnDate\" IS NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "IX_dormitories_SupervisorUserId",
                table: "dormitories",
                column: "SupervisorUserId");

            migrationBuilder.CreateIndex(
                name: "UX_dormitories_name",
                table: "dormitories",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.CreateIndex(
                name: "UX_dormitory_rooms_name",
                table: "dormitory_rooms",
                columns: new[] { "SchoolId", "DormitoryId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

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

            // --- Reprise des données Internat héritées (Enrollment.RoomId/BoardingStatus) ---
            // S'exécute sous le rôle propriétaire (exempté de RLS). Chaque instruction est idempotente. Les colonnes
            // héritées ne sont que LUES. Spec §4.1 (avec la correction « seule l'année active tient des lits »).
            migrationBuilder.Sql(BackfillDormitories);
            migrationBuilder.Sql(BackfillRooms);
            migrationBuilder.Sql(BackfillBeds);
            migrationBuilder.Sql(BackfillStays);
        }

        // 1. Pavillons : un par bâtiment vivant ayant au moins une chambre Dortoir vivante, Id = Building.Id.
        //    Genre déduit des élèves de l'année ACTIVE logés dans ses chambres ; ambigu ou vide → Mixte.
        private const string BackfillDormitories = """
            INSERT INTO dormitories ("Id","SchoolId","Name","Gender","CreatedAt","IsDeleted")
            SELECT b."Id", b."SchoolId", b."Name",
                   CASE g.gender WHEN 'M' THEN 'Garcons' WHEN 'F' THEN 'Filles' ELSE 'Mixte' END,
                   NOW(), FALSE
            FROM buildings b
            LEFT JOIN LATERAL (
                SELECT CASE WHEN count(DISTINCT s."Gender") = 1 THEN min(s."Gender") END AS gender
                FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                JOIN rooms r ON r."SchoolId" = e."SchoolId" AND r."Id" = e."RoomId"
                JOIN students s ON s."SchoolId" = e."SchoolId" AND s."Id" = e."StudentId"
                WHERE r."BuildingId" = b."Id" AND r."Type" = 'Dortoir' AND NOT r."IsDeleted"
                  AND e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
            ) g ON TRUE
            WHERE NOT b."IsDeleted"
              AND EXISTS (SELECT 1 FROM rooms r WHERE r."SchoolId" = b."SchoolId" AND r."BuildingId" = b."Id"
                          AND r."Type" = 'Dortoir' AND NOT r."IsDeleted")
            ON CONFLICT ("Id") DO NOTHING;
            """;

        // 2. Chambres : Id = Room.Id.
        private const string BackfillRooms = """
            INSERT INTO dormitory_rooms ("Id","SchoolId","DormitoryId","Name","CreatedAt","IsDeleted")
            SELECT r."Id", r."SchoolId", r."BuildingId", r."Name", NOW(), FALSE
            FROM rooms r
            JOIN dormitories d ON d."SchoolId" = r."SchoolId" AND d."Id" = r."BuildingId"
            WHERE r."Type" = 'Dortoir' AND NOT r."IsDeleted"
            ON CONFLICT ("Id") DO NOTHING;
            """;

        // 3. Lits : max(capacité, internes de l'année active) par chambre, numérotés 1..N.
        private const string BackfillBeds = """
            INSERT INTO beds ("Id","SchoolId","DormitoryRoomId","BedNumber","Status","CreatedAt","IsDeleted")
            SELECT gen_random_uuid(), dr."SchoolId", dr."Id", n::int, 'Available', NOW(), FALSE
            FROM dormitory_rooms dr
            JOIN rooms r ON r."SchoolId" = dr."SchoolId" AND r."Id" = dr."Id"
            CROSS JOIN LATERAL generate_series(1, GREATEST(r."Capacity", (
                SELECT count(*) FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId" AND y."IsActive" AND NOT y."IsDeleted"
                WHERE e."SchoolId" = r."SchoolId" AND e."RoomId" = r."Id" AND e."BoardingStatus" = 'Interne'
                  AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"))::int) AS n
            WHERE NOT EXISTS (SELECT 1 FROM beds b WHERE b."SchoolId" = dr."SchoolId" AND b."DormitoryRoomId" = dr."Id");
            """;

        // 4. Séjours. Année active → actif (lit de rang = ordre d'inscription parmi les SEULS internes de l'année active
        //    d'une même chambre — ni les demi-pensionnaires ni les années passées ne décalent le rang) ; autre année →
        //    clos à la fin de l'année, sans lit. Un DemiPensionnaire n'a jamais de lit (N7).
        private const string BackfillStays = """
            WITH legacy AS (
                SELECT e."Id" AS enrollment_id, e."SchoolId", e."StudentId", e."BoardingStatus" AS regime, e."RoomId",
                       e."EnrolledAt" AS enrolled_at, (e."EnrolledAt" AT TIME ZONE 'UTC')::date AS started,
                       y."IsActive" AS year_active, y."EndDate" AS year_end
                FROM enrollments e
                JOIN school_years y ON y."SchoolId" = e."SchoolId" AND y."Id" = e."SchoolYearId"
                WHERE e."BoardingStatus" <> 'Externe' AND e."Status" <> 'Cancelled' AND NOT e."IsDeleted"
            ),
            seated AS (
                SELECT enrollment_id,
                       row_number() OVER (PARTITION BY "RoomId" ORDER BY enrolled_at, enrollment_id) AS seat
                FROM legacy
                WHERE year_active AND regime = 'Interne' AND "RoomId" IS NOT NULL
            )
            INSERT INTO boarding_enrollments
                ("Id","SchoolId","StudentId","EnrollmentId","Regime","BedId","StartDate","EndDate","IsActive",
                 "AllowedExitPersons","CreatedAt","IsDeleted")
            SELECT gen_random_uuid(), l."SchoolId", l."StudentId", l.enrollment_id, l.regime, bed."Id",
                   l.started, CASE WHEN l.year_active THEN NULL ELSE GREATEST(l.year_end, l.started) END,
                   l.year_active, '[]'::jsonb, NOW(), FALSE
            FROM legacy l
            LEFT JOIN seated st ON st.enrollment_id = l.enrollment_id
            LEFT JOIN beds bed ON bed."SchoolId" = l."SchoolId" AND bed."DormitoryRoomId" = l."RoomId"
                              AND bed."BedNumber" = st.seat AND NOT bed."IsDeleted"
            WHERE NOT EXISTS (SELECT 1 FROM boarding_enrollments be
                              WHERE be."SchoolId" = l."SchoolId" AND be."EnrollmentId" = l.enrollment_id);
            """;

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "boarding_attendances");

            migrationBuilder.DropTable(
                name: "boarding_leaves");

            migrationBuilder.DropTable(
                name: "boarding_enrollments");

            migrationBuilder.DropTable(
                name: "beds");

            migrationBuilder.DropTable(
                name: "dormitory_rooms");

            migrationBuilder.DropTable(
                name: "dormitories");
        }
    }
}
