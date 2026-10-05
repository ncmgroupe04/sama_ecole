using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Revue finale de la tâche 2 (plan soft delete 2026-10-01) — <c>RevokeDeleteOnTeacherSubjects</c>
    /// révoquait déjà <c>DELETE</c> sur <c>teacher_subjects</c>, mais par un <c>REVOKE</c> NU, sans la
    /// garde « le rôle existe-t-il ? » que portent TOUS ses migrations sœurs (<c>GrantDeleteOnTeacherSubjects</c>,
    /// qui accordait ce même privilège plus tôt, et <c>RevokeDeleteOnBusinessTables</c>, qui révoque sur le
    /// reste des tables métier). Sur une base fraîche où les migrations s'exécuteraient avant la création du
    /// rôle applicatif, <c>RevokeDeleteOnTeacherSubjects</c> échouerait donc à l'étape REVOKE — alors que ses
    /// voisines s'en sortent par un simple <c>RAISE WARNING</c>.
    ///
    /// <c>RevokeDeleteOnTeacherSubjects</c> est déjà APPLIQUÉE (tâche 2, commit antérieur à cette revue) :
    /// AGENTS.md interdit d'y toucher. Cette migration est donc additive et REJOUE la même révocation,
    /// gardée cette fois, exactement comme <c>GrantDeleteOnTeacherSubjects</c> gardait son GRANT :
    ///   - sur une base où le rôle existe déjà (cas normal, y compris une base qui a déjà appliqué la
    ///     migration d'origine) : un second REVOKE DELETE est un no-op, PostgreSQL ne s'en plaint pas ;
    ///   - sur une base fraîche où le rôle n'existe pas encore au moment où ces migrations rejouent :
    ///     un simple avertissement, pas d'échec — exactement le filet que les migrations sœurs ont déjà.
    /// </summary>
    public partial class AddRoleGuardToTeacherSubjectsRevoke : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string Table = "teacher_subjects";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE DELETE ON {Table} FROM {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : rien à révoquer, {Table} n''a de toute façon aucun privilège à retirer pour l''instant.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <summary>
        /// Miroir du <c>Down()</c> d'origine (ré-accorde DELETE), gardé de la même façon. Roll-forward
        /// reste préféré pour le reste de la tâche 2 (précheck de doublons actifs sur l'index unique) :
        /// ce <c>Down()</c> ne fait que rendre le privilège, il ne touche à aucun index.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT DELETE ON {Table} TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
