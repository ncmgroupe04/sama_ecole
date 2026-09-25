using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Cahier de texte &amp; syllabus (Évolution N°7) — deux tables tenant : syllabus_units (le référentiel des
    /// chapitres d'une matière pour un niveau, DEMSGS/INEADE) et class_journal_entry_units (les chapitres cochés
    /// par l'enseignant dans une séance du cahier de texte).
    ///
    /// EF ne génère jamais les policies RLS : ajoutées à la main ci-dessous (AGENTS.md règle #2). UPDATE accordé
    /// (un chapitre se renomme, un lien se retire logiquement), aucun DELETE (règle #6).
    ///
    /// reset_school_data est corrigée dans la MÊME migration (technique de AddClassSubjectsAndOptions : insertion
    /// ancrée, échec si l'ancre est introuvable ou ambiguë, idempotence) : les deux tables ont une FK RESTRICT
    /// vers class_journal_entries et subjects, toutes deux purgées. Aucune n'est rattachée à une année scolaire :
    /// delete_school_year n'est pas concernée.
    /// </summary>
    public partial class AddSyllabusTracking : Migration
    {
        private static readonly string[] TenantTables = ["syllabus_units", "class_journal_entry_units"];

        private const string AppRole = "sama_ecole_app";

        // Les liens séance → chapitre avant les séances (class_journal_entries), puis les chapitres avant
        // « subjects », qui vient plus loin : les deux lignes s'insèrent juste avant class_journal_entries.
        private const string ResetLinksLine =
            "['class_journal_entry_units', 'Chapitres cochés au cahier de texte'],";
        private const string ResetUnitsLine =
            "['syllabus_units', 'Référentiel des programmes'],";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_class_journal_entries_SchoolId_Id",
                table: "class_journal_entries",
                columns: new[] { "SchoolId", "Id" });

            migrationBuilder.CreateTable(
                name: "syllabus_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Section = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    PlannedHours = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
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
                    table.PrimaryKey("PK_syllabus_units", x => x.Id);
                    table.UniqueConstraint("AK_syllabus_units_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.ForeignKey(
                        name: "FK_syllabus_units_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_syllabus_units_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "class_journal_entry_units",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassJournalEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SyllabusUnitId = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("PK_class_journal_entry_units", x => x.Id);
                    table.ForeignKey(
                        name: "FK_class_journal_entry_units_class_journal_entries_SchoolId_Cl~",
                        columns: x => new { x.SchoolId, x.ClassJournalEntryId },
                        principalTable: "class_journal_entries",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_journal_entry_units_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_class_journal_entry_units_syllabus_units_SchoolId_SyllabusU~",
                        columns: x => new { x.SchoolId, x.SyllabusUnitId },
                        principalTable: "syllabus_units",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_journal_entry_units_SchoolId",
                table: "class_journal_entry_units",
                column: "SchoolId");

            migrationBuilder.CreateIndex(
                name: "IX_class_journal_entry_units_SchoolId_ClassJournalEntryId",
                table: "class_journal_entry_units",
                columns: new[] { "SchoolId", "ClassJournalEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_class_journal_entry_units_SchoolId_SyllabusUnitId",
                table: "class_journal_entry_units",
                columns: new[] { "SchoolId", "SyllabusUnitId" });

            migrationBuilder.CreateIndex(
                name: "UX_class_journal_entry_units_link",
                table: "class_journal_entry_units",
                columns: new[] { "ClassJournalEntryId", "SyllabusUnitId" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            migrationBuilder.CreateIndex(
                name: "UX_syllabus_units_title",
                table: "syllabus_units",
                columns: new[] { "SchoolId", "SubjectId", "GradeLevel", "Title" },
                unique: true,
                filter: "NOT \"IsDeleted\"");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");

                migrationBuilder.Sql($$"""
                    CREATE POLICY {{table}}_tenant_isolation ON "{{table}}"
                        USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                        WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                    """);

                // Aucun DELETE : le soft delete n'émet jamais de SQL DELETE (règle #6).
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

            // --- Purge : reset_school_data (les liens d'abord, puis les chapitres, avant class_journal_entries) ---
            migrationBuilder.Sql(PatchResetSchoolData("['class_journal_entries',", ResetUnitsLine, "syllabus_units"));
            migrationBuilder.Sql(PatchResetSchoolData(ResetUnitsLine, ResetLinksLine, "class_journal_entry_units"));
        }

        /// <summary>
        /// Insère <paramref name="line"/> juste avant <paramref name="anchor"/> dans reset_school_data. Échoue si
        /// l'ancre est introuvable ou ambiguë ; ne fait rien si <paramref name="marker"/> y figure déjà.
        /// </summary>
        private static string PatchResetSchoolData(string anchor, string line, string marker) => $$"""
            DO $patch$
            DECLARE
                v_def    text;
                v_anchor text := {{Quote(anchor)}};
            BEGIN
                v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                IF position({{Quote("'" + marker + "'")}} IN v_def) > 0 THEN
                    RETURN;
                END IF;

                IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                    RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                END IF;

                EXECUTE replace(v_def, v_anchor,
                    {{Quote(line)}} || chr(10) || '                        ' || v_anchor);
            END
            $patch$;
            """;

        /// <summary>Retire exactement ce que <see cref="PatchResetSchoolData"/> a inséré.</summary>
        private static string UnpatchResetSchoolData(string line) => $$"""
            DO $patch$
            DECLARE
                v_def text;
            BEGIN
                v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
                EXECUTE replace(v_def, {{Quote(line)}} || chr(10) || '                        ', '');
            END
            $patch$;
            """;

        /// <summary>Littéral SQL : apostrophes doublées.</summary>
        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(UnpatchResetSchoolData(ResetLinksLine));
            migrationBuilder.Sql(UnpatchResetSchoolData(ResetUnitsLine));

            foreach (var table in TenantTables)
            {
                migrationBuilder.Sql($"DROP POLICY IF EXISTS {table}_tenant_isolation ON \"{table}\";");
            }

            migrationBuilder.DropTable(
                name: "class_journal_entry_units");

            migrationBuilder.DropTable(
                name: "syllabus_units");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_class_journal_entries_SchoolId_Id",
                table: "class_journal_entries");
        }
    }
}
