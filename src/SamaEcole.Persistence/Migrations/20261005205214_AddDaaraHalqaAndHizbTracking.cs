using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Socle de données Internat/Daara : <c>instructors</c> (Oustaz), <c>students.InstructorId</c>
    /// (rattachement à la Halqa, nullable, FK composite SchoolId+InstructorId) et
    /// <c>student_hizb_statuses</c> (suivi coranique par Hizb). Comme pour AddQuranCoreModule, EF ne
    /// génère jamais les policies RLS : elles sont ajoutées à la main ci-dessous (AGENTS.md règle #2),
    /// et RlsCoverageTests échoue si elles manquent. Aucun DELETE accordé : soft delete uniquement
    /// (règle #6). Les deux tables sont aussi inscrites dans reset_school_data — FK RESTRICT vers
    /// <c>students</c> / <c>instructors</c>, sinon « Réinitialiser les données » échoue en 23503.
    /// </summary>
    public partial class AddDaaraHalqaAndHizbTracking : Migration
    {
        private static readonly string[] TenantTables = ["instructors", "student_hizb_statuses"];

        private const string AppRole = "sama_ecole_app";

        // reset_school_data : le suivi par Hizb précède students (FK RESTRICT vers students) ; les Oustaz
        // suivent students (students.InstructorId → instructors) et précèdent teachers, ancre voisine stable.
        private const string HizbAnchor = "['students',";

        private const string HizbInsert =
            "['student_hizb_statuses', 'Suivi coranique par Hizb'],\n                        ";

        private const string InstructorAnchor = "['teachers',";

        private const string InstructorInsert =
            "['instructors', 'Oustaz (responsables de Halqa)'],\n                        ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InstructorId",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "instructors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FullNameAr = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Active"),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
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
                    table.PrimaryKey("PK_instructors", x => x.Id);
                    table.UniqueConstraint("AK_instructors_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_instructors_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_instructors_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_hizb_statuses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    HizbNumber = table.Column<int>(type: "integer", nullable: false),
                    CompletedQuarters = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "NotStarted"),
                    LastEvaluatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Rating = table.Column<int>(type: "integer", nullable: true),
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
                    table.PrimaryKey("PK_student_hizb_statuses", x => x.Id);
                    table.CheckConstraint("CK_student_hizb_statuses_hizb_range", "\"HizbNumber\" BETWEEN 1 AND 60");
                    table.CheckConstraint("CK_student_hizb_statuses_quarters_range", "\"CompletedQuarters\" BETWEEN 0 AND 4");
                    table.CheckConstraint("CK_student_hizb_statuses_rating_range", "\"Rating\" IS NULL OR \"Rating\" BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_student_hizb_statuses_state_matches_quarters", "(\"State\" = 'NotStarted' AND \"CompletedQuarters\" = 0) OR (\"State\" = 'InProgress' AND \"CompletedQuarters\" BETWEEN 1 AND 3) OR (\"State\" = 'Completed' AND \"CompletedQuarters\" = 4)");
                    table.ForeignKey(
                        name: "FK_student_hizb_statuses_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_student_hizb_statuses_students_SchoolId_StudentId",
                        columns: x => new { x.SchoolId, x.StudentId },
                        principalTable: "students",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_students_SchoolId_InstructorId",
                table: "students",
                columns: new[] { "SchoolId", "InstructorId" });

            migrationBuilder.CreateIndex(
                name: "IX_instructors_SchoolId",
                table: "instructors",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "UX_instructors_UserId",
                table: "instructors",
                column: "UserId",
                unique: true,
                filter: "\"UserId\" IS NOT NULL AND NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UX_student_hizb_statuses_key",
                table: "student_hizb_statuses",
                columns: new[] { "SchoolId", "StudentId", "HizbNumber" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.AddForeignKey(
                name: "FK_students_instructors_SchoolId_InstructorId",
                table: "students",
                columns: new[] { "SchoolId", "InstructorId" },
                principalTable: "instructors",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

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

            // --- Purge « Réinitialiser les données » ---
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", HizbAnchor, HizbInsert, "student_hizb_statuses"));
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", InstructorAnchor, InstructorInsert, "instructors"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", InstructorInsert + InstructorAnchor, InstructorAnchor, "instructors"));
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", HizbInsert + HizbAnchor, HizbAnchor, "student_hizb_statuses"));

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropForeignKey(
                name: "FK_students_instructors_SchoolId_InstructorId",
                table: "students");

            migrationBuilder.DropTable(
                name: "instructors");

            migrationBuilder.DropTable(
                name: "student_hizb_statuses");

            migrationBuilder.DropIndex(
                name: "IX_students_SchoolId_InstructorId",
                table: "students");

            migrationBuilder.DropColumn(
                name: "InstructorId",
                table: "students");
        }

        // Même technique que AddStudentSubjectExemptionsToPurges : on lit la définition COURANTE de la fonction
        // (pg_get_functiondef), on y insère UNE étape à un endroit ancré, et on la recrée. Ancre absente ou
        // ambiguë : la migration ÉCHOUE ; étape déjà présente : elle ne fait rien. Textes passés dans des
        // littéraux dollar-quotés : aucun échappement d'apostrophe à gérer.
        private static string InsertBefore(string function, string anchor, string inserted, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def    text;
                v_anchor text := $a${{anchor}}$a$;
                v_ins    text := $i${{inserted}}$i$;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('{{marker}}' IN v_def) > 0 THEN
                    RETURN;
                END IF;

                IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                    RAISE EXCEPTION '{{function}} : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                END IF;

                EXECUTE replace(v_def, v_anchor, v_ins || v_anchor);
            END
            $patch$;
            """;

        private static string Remove(string function, string with, string without, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def text;
            BEGIN
                v_def := pg_get_functiondef('{{function}}'::regprocedure);

                IF position('{{marker}}' IN v_def) = 0 THEN
                    RETURN;
                END IF;

                EXECUTE replace(v_def, $w${{with}}$w$, $o${{without}}$o$);
            END
            $patch$;
            """;
    }
}
