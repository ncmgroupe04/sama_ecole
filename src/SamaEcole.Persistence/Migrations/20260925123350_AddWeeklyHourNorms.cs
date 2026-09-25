using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Volumes horaires de référence (Évolution N°7) — une table tenant de paramétrage : weekly_hour_norms (volume
    /// hebdomadaire d'une matière pour un niveau et, au lycée, une série, quand l'école s'écarte de la grille codée).
    ///
    /// EF ne génère jamais les policies RLS : ajoutées à la main (AGENTS.md règle #2). UPDATE accordé (un réglage se
    /// corrige, « Revenir à la référence » l'archive), aucun DELETE (règle #6). FK RESTRICT vers subjects, purgée par
    /// « Réinitialiser les données » : reset_school_data est corrigée dans la MÊME migration (technique de
    /// AddClassSubjectsAndOptions), la table vidée juste avant « subjects ».
    /// </summary>
    public partial class AddWeeklyHourNorms : Migration
    {
        private const string Table = "weekly_hour_norms";

        private const string AppRole = "sama_ecole_app";

        private const string ResetAnchor = "['subjects',";
        private const string ResetLine = "['weekly_hour_norms', 'Volumes horaires de l''établissement'],";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "weekly_hour_norms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradeLevel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeeklyHours = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false),
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
                    table.PrimaryKey("PK_weekly_hour_norms", x => x.Id);
                    table.CheckConstraint("CK_weekly_hour_norms_hours", "\"WeeklyHours\" >= 0 AND \"WeeklyHours\" <= 40");
                    table.ForeignKey(
                        name: "FK_weekly_hour_norms_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_weekly_hour_norms_subjects_SchoolId_SubjectId",
                        columns: x => new { x.SchoolId, x.SubjectId },
                        principalTable: "subjects",
                        principalColumns: new[] { "SchoolId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_weekly_hour_norms_SchoolId_SubjectId",
                table: "weekly_hour_norms",
                columns: new[] { "SchoolId", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "UX_weekly_hour_norms_scope",
                table: "weekly_hour_norms",
                columns: new[] { "SchoolId", "GradeLevel", "Series", "SubjectId" },
                unique: true,
                filter: "NOT \"IsDeleted\"")
                .Annotation("Npgsql:NullsDistinct", false);

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

            // --- Purge : reset_school_data (juste avant « subjects », que la table référence) ---
            migrationBuilder.Sql($$"""
                DO $patch$
                DECLARE
                    v_def    text;
                    v_anchor text := {{Quote(ResetAnchor)}};
                BEGIN
                    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);

                    IF position({{Quote("'" + Table + "'")}} IN v_def) > 0 THEN
                        RETURN;
                    END IF;

                    IF array_length(string_to_array(v_def, v_anchor), 1) <> 2 THEN
                        RAISE EXCEPTION 'reset_school_data : ancre % introuvable ou ambiguë — la fonction a changé de forme, corriger cette migration.', v_anchor;
                    END IF;

                    EXECUTE replace(v_def, v_anchor, {{Quote(ResetLine)}} || chr(10) || '                        ' || v_anchor);
                END
                $patch$;
                """);
        }

        /// <summary>Littéral SQL : apostrophes doublées.</summary>
        private static string Quote(string value) => "'" + value.Replace("'", "''") + "'";


        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $patch$
                DECLARE
                    v_def text;
                BEGIN
                    v_def := pg_get_functiondef('reset_school_data(uuid)'::regprocedure);
                    EXECUTE replace(v_def, {{Quote(ResetLine)}} || chr(10) || '                        ', '');
                END
                $patch$;
                """);

            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON \"{Table}\";");

            migrationBuilder.DropTable(
                name: "weekly_hour_norms");
        }
    }
}
