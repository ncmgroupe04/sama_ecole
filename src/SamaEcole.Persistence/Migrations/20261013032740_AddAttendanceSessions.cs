using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Pointage par sessions configurables : nouvelle table <c>attendance_sessions</c> (Appel de nuit, repas, Appel de la Halqa…) et
    /// extension de <c>boarding_attendances</c> (pas de nouvelle table de pointages) : <c>SessionId</c> et <c>StudentId</c>
    /// obligatoires, <c>BoardingEnrollmentId</c> devenu facultatif (une session de Halqa ne passe pas par un séjour), unicité
    /// <c>(StudentId, SessionId, Date)</c> à la place de <c>(séjour, date)</c>.
    ///
    /// <b>Reprise</b> : toute école qui avait déjà des pointages reçoit une session « Appel de nuit » (internes, tous les jours) à
    /// laquelle toutes ses lignes sont rattachées ; <c>StudentId</c> est déduit du séjour. Comportement d'origine conservé à l'identique.
    ///
    /// <b>Déploiement progressif</b> : pendant que la migration est passée mais que l'ancienne révision tourne encore, celle-ci insère des
    /// pointages SANS <c>SessionId</c> ni <c>StudentId</c>. Un déclencheur BEFORE INSERT les renseigne (élève du séjour, session
    /// « Appel de nuit » de l'école, créée au besoin) : l'ancienne révision continue de pointer sans erreur.
    ///
    /// <b>Retour arrière</b> (<c>Down</c>) : l'ancien modèle ne sait représenter que l'appel de nuit d'un séjour. Les pointages qu'il ne sait
    /// pas porter (autres sessions, Halqa) sont SUPPRIMÉS : à ne jouer qu'après sauvegarde, et à documenter dans le runbook de la PR.
    ///
    /// EF ne génère jamais la RLS : elle est ajoutée à la main (AGENTS.md règle #2). La table est inscrite dans <c>reset_school_data</c>
    /// (FK RESTRICT depuis les pointages).
    /// </summary>
    public partial class AddAttendanceSessions : Migration
    {
        private const string Table = "attendance_sessions";

        private const string AppRole = "sama_ecole_app";

        // reset_school_data : les sessions suivent les pointages (FK RESTRICT) ; même ancre que les sorties.
        private const string LeavesAnchor = "['boarding_leaves',";

        private const string SessionsInsert =
            "['attendance_sessions', 'Sessions de pointage'],\n                        ";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_sessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Days = table.Column<int>(type: "integer", nullable: false),
                    TimeLabel = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
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
                    table.PrimaryKey("PK_attendance_sessions", x => x.Id);
                    table.UniqueConstraint("AK_attendance_sessions_SchoolId_Id", x => new { x.SchoolId, x.Id });
                    table.CheckConstraint("CK_attendance_sessions_days", "\"Days\" BETWEEN 1 AND 127");
                    table.ForeignKey(
                        name: "FK_attendance_sessions_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_attendance_sessions_name",
                table: "attendance_sessions",
                columns: new[] { "SchoolId", "Name" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Suppression logique seulement (règle #6) : SELECT, INSERT, UPDATE — jamais DELETE.
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

            // --- Pointages : colonnes d'abord NULLABLES, reprise, puis contraintes ---
            migrationBuilder.DropIndex(
                name: "UX_boarding_attendances_night",
                table: "boarding_attendances");

            migrationBuilder.AlterColumn<Guid>(
                name: "BoardingEnrollmentId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<Guid>(
                name: "SessionId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "StudentId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: true);

            // Reprise (migration exécutée sous le rôle propriétaire, avant toute RLS applicative) : une session « Appel de nuit » par
            // école ayant déjà des pointages, puis rattachement de toutes les lignes (soft-deleted comprises : la colonne devient NOT NULL).
            migrationBuilder.Sql("""
                INSERT INTO attendance_sessions
                    ("Id", "SchoolId", "Name", "Audience", "Days", "TimeLabel", "IsActive", "SortOrder", "CreatedAt", "IsDeleted")
                SELECT gen_random_uuid(), x."SchoolId", 'Appel de nuit', 'Interns', 127, NULL, TRUE, 0, now(), FALSE
                FROM (SELECT DISTINCT "SchoolId" FROM boarding_attendances) x;

                UPDATE boarding_attendances a
                SET "StudentId" = be."StudentId"
                FROM boarding_enrollments be
                WHERE be."Id" = a."BoardingEnrollmentId" AND be."SchoolId" = a."SchoolId";

                UPDATE boarding_attendances a
                SET "SessionId" = s."Id"
                FROM attendance_sessions s
                WHERE s."SchoolId" = a."SchoolId" AND s."Name" = 'Appel de nuit' AND s."Audience" = 'Interns' AND NOT s."IsDeleted";
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SessionId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "StudentId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_boarding_attendances_SchoolId_SessionId_Date",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "SessionId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_boarding_attendances_SchoolId_StudentId",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "StudentId" });

            migrationBuilder.CreateIndex(
                name: "UX_boarding_attendances_session_day",
                table: "boarding_attendances",
                columns: new[] { "StudentId", "SessionId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");

            migrationBuilder.AddForeignKey(
                name: "FK_boarding_attendances_attendance_sessions_SchoolId_SessionId",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "SessionId" },
                principalTable: "attendance_sessions",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_boarding_attendances_students_SchoolId_StudentId",
                table: "boarding_attendances",
                columns: new[] { "SchoolId", "StudentId" },
                principalTable: "students",
                principalColumns: new[] { "SchoolId", "Id" },
                onDelete: ReferentialAction.Restrict);

            // --- Compatibilité de déploiement : l'ancienne révision insère sans SessionId ni StudentId ---
            // BEFORE INSERT : s'exécute avant la vérification NOT NULL et l'index unique. Exécuté avec les droits de l'appelant (le rôle
            // applicatif a SELECT sur les séjours et INSERT sur les sessions) : la RLS s'applique, l'école est celle de la ligne.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION boarding_attendances_fill_defaults() RETURNS trigger
                LANGUAGE plpgsql AS $fn$
                BEGIN
                    IF NEW."StudentId" IS NULL AND NEW."BoardingEnrollmentId" IS NOT NULL THEN
                        SELECT be."StudentId" INTO NEW."StudentId"
                        FROM boarding_enrollments be
                        WHERE be."Id" = NEW."BoardingEnrollmentId" AND be."SchoolId" = NEW."SchoolId";
                    END IF;

                    IF NEW."SessionId" IS NULL THEN
                        SELECT s."Id" INTO NEW."SessionId"
                        FROM attendance_sessions s
                        WHERE s."SchoolId" = NEW."SchoolId" AND s."Audience" = 'Interns' AND NOT s."IsDeleted"
                        ORDER BY s."IsActive" DESC, s."SortOrder", s."Name"
                        LIMIT 1;

                        IF NEW."SessionId" IS NULL THEN
                            INSERT INTO attendance_sessions
                                ("Id", "SchoolId", "Name", "Audience", "Days", "IsActive", "SortOrder", "CreatedAt", "IsDeleted")
                            VALUES (gen_random_uuid(), NEW."SchoolId", 'Appel de nuit', 'Interns', 127, TRUE, 0, now(), FALSE)
                            RETURNING "Id" INTO NEW."SessionId";
                        END IF;
                    END IF;

                    RETURN NEW;
                END
                $fn$;

                CREATE TRIGGER boarding_attendances_fill_defaults
                    BEFORE INSERT ON boarding_attendances
                    FOR EACH ROW EXECUTE FUNCTION boarding_attendances_fill_defaults();
                """);

            // --- Purge « Réinitialiser les données » : les sessions suivent les pointages (FK RESTRICT) ---
            migrationBuilder.Sql(InsertBefore("reset_school_data(uuid)", LeavesAnchor, SessionsInsert, Table));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Remove("reset_school_data(uuid)", SessionsInsert + LeavesAnchor, LeavesAnchor, Table));

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS boarding_attendances_fill_defaults ON boarding_attendances;
                DROP FUNCTION IF EXISTS boarding_attendances_fill_defaults();
                """);

            // L'ancien modèle ne porte que l'appel de nuit d'un séjour : on retire ce qu'il ne sait pas représenter (autres sessions,
            // appels de Halqa). Sauvegarde obligatoire avant de jouer ce retour arrière.
            migrationBuilder.Sql("""
                DELETE FROM boarding_attendances a
                WHERE a."BoardingEnrollmentId" IS NULL
                   OR a."SessionId" IS DISTINCT FROM (
                        SELECT s."Id" FROM attendance_sessions s
                        WHERE s."SchoolId" = a."SchoolId" AND s."Audience" = 'Interns' AND NOT s."IsDeleted"
                        ORDER BY s."IsActive" DESC, s."SortOrder", s."Name"
                        LIMIT 1);
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_boarding_attendances_attendance_sessions_SchoolId_SessionId",
                table: "boarding_attendances");

            migrationBuilder.DropForeignKey(
                name: "FK_boarding_attendances_students_SchoolId_StudentId",
                table: "boarding_attendances");

            migrationBuilder.Sql($"DROP POLICY IF EXISTS {Table}_tenant_isolation ON \"{Table}\";");

            migrationBuilder.DropTable(
                name: "attendance_sessions");

            migrationBuilder.DropIndex(
                name: "IX_boarding_attendances_SchoolId_SessionId_Date",
                table: "boarding_attendances");

            migrationBuilder.DropIndex(
                name: "IX_boarding_attendances_SchoolId_StudentId",
                table: "boarding_attendances");

            migrationBuilder.DropIndex(
                name: "UX_boarding_attendances_session_day",
                table: "boarding_attendances");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "boarding_attendances");

            migrationBuilder.DropColumn(
                name: "StudentId",
                table: "boarding_attendances");

            migrationBuilder.AlterColumn<Guid>(
                name: "BoardingEnrollmentId",
                table: "boarding_attendances",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "UX_boarding_attendances_night",
                table: "boarding_attendances",
                columns: new[] { "BoardingEnrollmentId", "Date" },
                unique: true,
                filter: "\"IsDeleted\" = false");
        }

        // Même technique que AddHizbEvaluationJournal : on lit la définition COURANTE de la fonction (pg_get_functiondef), on y insère
        // UNE étape à un endroit ancré, et on la recrée. Ancre absente ou ambiguë : la migration ÉCHOUE ; étape déjà présente : rien.
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
