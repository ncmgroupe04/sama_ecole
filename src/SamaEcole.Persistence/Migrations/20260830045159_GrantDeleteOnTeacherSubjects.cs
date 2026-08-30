using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-T01 — accorde <c>DELETE</c> sur <c>teacher_subjects</c> au rôle applicatif.
    ///
    /// CONTEXTE. La migration <c>AddTeachers</c> avait volontairement limité le rôle à
    /// <c>SELECT, INSERT, UPDATE</c> (« le soft delete est un UPDATE »). Mais
    /// <c>UpdateTeacherCommandHandler</c> retire une qualification en supprimant PHYSIQUEMENT la ligne
    /// (<c>dbContext.TeacherSubjects.Remove(...)</c>) : décocher une matière sur la fiche enseignant
    /// échouait donc en <c>42501: permission denied for table teacher_subjects</c> (HTTP 500).
    ///
    /// POURQUOI LE GRANT PLUTÔT QUE LE SOFT DELETE (option A du ticket). <c>teacher_subjects</c> est
    /// une pure table de LIAISON : elle porte « cet enseignant est qualifié pour cette matière »,
    /// rien d'autre. « Untel a été qualifié pour les maths jusqu'en mars » n'a aucune valeur d'audit,
    /// et le rétablir plus tard doit simplement recréer la ligne (le retrait libère la place — cf.
    /// l'index unique (TeacherId, SubjectId)). Le <c>DELETE</c> est déjà accordé sur d'autres tables
    /// de même nature (<c>refresh_tokens</c>, <c>classrooms</c>, les tables de <c>AddPayrollAndTax</c>
    /// / <c>AddScheduleAndDisbursements</c>) : ce grant n'ouvre pas une exception, il aligne
    /// <c>teacher_subjects</c> sur elles. Les tables portant une donnée métier historisée
    /// (élèves, notes, paiements…) restent, elles, sans <c>DELETE</c> — l'invariant de la règle #6
    /// pour ces tables-là est intact.
    ///
    /// Additive et réversible : <c>Down()</c> retire le <c>DELETE</c> et rend la table à
    /// <c>SELECT, INSERT, UPDATE</c>.
    /// </summary>
    public partial class GrantDeleteOnTeacherSubjects : Migration
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
                        EXECUTE 'GRANT DELETE ON {Table} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : le retrait d''une matière d''enseignant restera bloqué.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE DELETE ON {Table} FROM {AppRole}';
                    END IF;
                END
                $$;
                """);
        }
    }
}
