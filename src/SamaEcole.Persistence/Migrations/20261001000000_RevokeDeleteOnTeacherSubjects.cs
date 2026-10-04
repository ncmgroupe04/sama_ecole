using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Preserves revoked teacher-subject qualifications as auditable history and removes the
    /// application role's physical-delete permission introduced by the older additive migration.
    ///
    /// Precheck: the unique index becomes partial (active rows only). Before replacing it, the migration
    /// looks for active duplicates that would violate the new index and stops with an explicit error
    /// instead of choosing, merging or deleting any row.
    /// </summary>
    public partial class RevokeDeleteOnTeacherSubjects : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string Table = "teacher_subjects";
        private const string ActiveIndex = "IX_teacher_subjects_TeacherId_SubjectId";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $$
                DECLARE conflicts integer;
                BEGIN
                    SELECT count(*) INTO conflicts FROM (
                        SELECT 1 FROM {{Table}}
                        WHERE NOT "IsDeleted"
                        GROUP BY "TeacherId", "SubjectId"
                        HAVING count(*) > 1
                    ) c;
                    IF conflicts > 0 THEN
                        RAISE EXCEPTION 'teacher_subjects: % active (TeacherId, SubjectId) duplicate group(s); resolve them by an approved business action before migrating', conflicts;
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql($"DROP INDEX IF EXISTS \"{ActiveIndex}\";");
            migrationBuilder.Sql($"CREATE UNIQUE INDEX \"{ActiveIndex}\" ON {Table} (\"TeacherId\", \"SubjectId\") WHERE NOT \"IsDeleted\";");
            migrationBuilder.Sql($"REVOKE DELETE ON {Table} FROM {AppRole};");
        }

        /// <summary>
        /// Roll-forward is preferred: once several tombstones of one pair exist, the old unfiltered unique
        /// index cannot be rebuilt. The downgrade refuses explicitly in that case rather than failing midway
        /// or deleting history.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1 FROM {{Table}} GROUP BY "TeacherId", "SubjectId" HAVING count(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'teacher_subjects: revoked history contains several rows per (TeacherId, SubjectId); downgrade refused, roll forward instead';
                    END IF;
                END $$;
                """);
            migrationBuilder.Sql($"DROP INDEX IF EXISTS \"{ActiveIndex}\";");
            migrationBuilder.Sql($"CREATE UNIQUE INDEX \"{ActiveIndex}\" ON {Table} (\"TeacherId\", \"SubjectId\");");
            migrationBuilder.Sql($"GRANT DELETE ON {Table} TO {AppRole};");
        }
    }
}
