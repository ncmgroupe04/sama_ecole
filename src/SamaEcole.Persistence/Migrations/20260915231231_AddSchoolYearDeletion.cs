using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// <c>delete_school_year(p_school_id, p_school_year_id)</c> — suppression d'une année scolaire ET
    /// de tout ce qui s'y rattache, EN MODE TEST UNIQUEMENT (arbitrage du 15/09/2026).
    ///
    /// Pourquoi une fonction et pas un <c>ExecuteDelete</c> EF Core : exactement les mêmes raisons que
    /// <c>reset_school_data</c> (migration AddSchoolDataReset) — le rôle applicatif n'a aucun droit de
    /// DELETE sur les tables métier, la purge doit atteindre les lignes en SUPPRESSION LOGIQUE que le
    /// Global Query Filter masque, et l'ordre des clés étrangères est une connaissance de la base.
    ///
    /// EN MODE RÉEL, cette fonction refuse : une année qui a servi porte des inscriptions, des reçus
    /// remis aux parents et des écritures comptables, que la règle #6 d'AGENTS.md déclare inaltérables.
    /// Le Handler (DeleteSchoolYearCommandHandler) n'appelle alors pas cette fonction du tout : il
    /// vérifie que l'année ne porte AUCUNE donnée, et se contente d'une suppression LOGIQUE. Le garde
    /// est dupliqué ici, comme pour reset_school_data, pour que la base tienne l'invariant même devant
    /// un futur appelant qui aurait oublié de le rejouer (règle #2, « les deux, jamais un seul »).
    ///
    /// PÉRIMÈTRE : tout ce qui porte l'année, directement (SchoolYearId) ou par ricochet — inscriptions
    /// et leur chaîne financière (paiements, ventilations, échéanciers, engagements, relances), appels
    /// et présences, notes et appréciations via les trimestres, affectations d'enseignants, examens,
    /// certificats de mutation, trimestres, puis l'année elle-même. Les ÉLÈVES ne sont PAS supprimés :
    /// ils appartiennent à l'établissement, pas à un exercice — seule leur inscription à cette année
    /// part. Les sessions de caisse non plus : elles couvrent une journée, pas une année.
    /// </summary>
    public partial class AddSchoolYearDeletion : Migration
    {
        private const string AppRole = "sama_ecole_app";
        private const string Signature = "delete_school_year(uuid, uuid)";

        /// <summary>
        /// Une étape de la purge : l'ordre SQL, puis le report de son nombre de lignes au Directeur.
        /// Chaque ordre porte AUSSI <c>"SchoolId" = p_school_id</c> — redondant avec la jointure sur
        /// l'année, et voulu : une ligne mal rattachée ne doit pas pouvoir s'effacer par ricochet.
        /// </summary>
        private static string Step(string sql, string label) => $"""
                    {sql.TrimEnd()}
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := '{label.Replace("'", "''")}'; rows_deleted := v_deleted; RETURN NEXT;

            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                CREATE OR REPLACE FUNCTION delete_school_year(p_school_id uuid, p_school_year_id uuid)
                RETURNS TABLE (label text, rows_deleted integer)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_session_school uuid;
                    v_went_live timestamptz;
                    v_deleted integer;
                    v_batches uuid[];
                BEGIN
                    v_session_school := NULLIF(current_setting('app.current_school_id', true), '')::uuid;

                    -- Aucune session tenant (tâche de fond, appel non authentifié) : échec en FERMETURE,
                    -- comme les policies RLS — un SECURITY DEFINER en est exempté, il se garde lui-même.
                    IF v_session_school IS NULL THEN
                        RAISE EXCEPTION 'delete_school_year : aucun établissement dans la session (app.current_school_id absent).'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF v_session_school <> p_school_id THEN
                        RAISE EXCEPTION 'delete_school_year : l''établissement visé (%) n''est pas celui de la session (%).',
                            p_school_id, v_session_school
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- L'année doit appartenir à CETTE école. Contrôle distinct du précédent : sans lui,
                    -- une session légitime pourrait viser l'année d'un confrère en connaissant son id.
                    IF NOT EXISTS (
                        SELECT 1 FROM school_years
                        WHERE "Id" = p_school_year_id AND "SchoolId" = p_school_id)
                    THEN
                        RAISE EXCEPTION 'delete_school_year : année scolaire % introuvable dans l''établissement %.',
                            p_school_year_id, p_school_id
                            USING ERRCODE = 'no_data_found';
                    END IF;

                    -- Mode réel : la suppression physique n'est plus une option (voir le résumé).
                    SELECT s."WentLiveAt" INTO v_went_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_went_live IS NOT NULL THEN
                        RAISE EXCEPTION
                            'SCHOOL_YEAR_DELETE_UNAVAILABLE_LIVE_MODE : l''établissement % est passé en mode réel le % ; les données de ses années scolaires sont comptables et ne peuvent plus être effacées.',
                            p_school_id, v_went_live
                            USING ERRCODE = 'raise_exception';
                    END IF;

                {{Step("""
                    DELETE FROM payment_breakdowns pb
                    USING payments p, enrollments e
                    WHERE pb."PaymentId" = p."Id" AND p."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND pb."SchoolId" = p_school_id;
                """, "Ventilations de paiement")}}
                {{Step("""
                    DELETE FROM payments p
                    USING enrollments e
                    WHERE p."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND p."SchoolId" = p_school_id;
                """, "Paiements")}}
                    -- Les lots de relance ne portent pas d'année : on ne supprime que ceux dont il ne
                    -- reste AUCUNE ligne une fois celles de l'exercice parties.
                    WITH gone AS (
                        DELETE FROM debtor_reminder_batch_items i
                        USING enrollments e
                        WHERE i."EnrollmentId" = e."Id"
                          AND e."SchoolYearId" = p_school_year_id AND i."SchoolId" = p_school_id
                        RETURNING i."DebtorReminderBatchId" AS batch_id)
                    SELECT COUNT(*)::integer, COALESCE(array_agg(DISTINCT batch_id), '{}')
                    INTO v_deleted, v_batches
                    FROM gone;

                    label := 'Relances de débiteurs (lignes)'; rows_deleted := v_deleted; RETURN NEXT;

                {{Step("""
                    DELETE FROM debtor_reminder_batches b
                    WHERE b."Id" = ANY (v_batches) AND b."SchoolId" = p_school_id
                      AND NOT EXISTS (
                          SELECT 1 FROM debtor_reminder_batch_items i
                          WHERE i."DebtorReminderBatchId" = b."Id");
                """, "Relances de débiteurs (lots)")}}
                {{Step("""
                    DELETE FROM fee_installments fi
                    USING fee_installment_plans fp, enrollments e
                    WHERE fi."FeeInstallmentPlanId" = fp."Id" AND fp."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND fi."SchoolId" = p_school_id;
                """, "Échéances")}}
                {{Step("""
                    DELETE FROM fee_installment_plans fp
                    USING enrollments e
                    WHERE fp."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND fp."SchoolId" = p_school_id;
                """, "Échéanciers")}}
                {{Step("""
                    DELETE FROM "FinancialCommitments" fc
                    USING enrollments e
                    WHERE fc."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND fc."SchoolId" = p_school_id;
                """, "Engagements financiers")}}
                {{Step("""
                    DELETE FROM enrollment_fee_lines l
                    USING enrollments e
                    WHERE l."EnrollmentId" = e."Id"
                      AND e."SchoolYearId" = p_school_year_id AND l."SchoolId" = p_school_id;
                """, "Lignes de frais d'inscription")}}
                {{Step("""
                    DELETE FROM enrollments
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Inscriptions")}}
                {{Step("""
                    DELETE FROM student_attendances sa
                    USING attendance_sheets s
                    WHERE sa."AttendanceSheetId" = s."Id"
                      AND s."SchoolYearId" = p_school_year_id AND sa."SchoolId" = p_school_id;
                """, "Présences des élèves")}}
                {{Step("""
                    DELETE FROM attendance_sheets
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Fiches d'appel")}}
                {{Step("""
                    DELETE FROM grades g
                    USING terms t
                    WHERE g."TermId" = t."Id"
                      AND t."SchoolYearId" = p_school_year_id AND g."SchoolId" = p_school_id;
                """, "Notes")}}
                {{Step("""
                    DELETE FROM report_card_remarks r
                    USING terms t
                    WHERE r."TermId" = t."Id"
                      AND t."SchoolYearId" = p_school_year_id AND r."SchoolId" = p_school_id;
                """, "Appréciations de bulletin")}}
                {{Step("""
                    DELETE FROM teacher_assignments
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Affectations des enseignants")}}
                {{Step("""
                    DELETE FROM exam_results r
                    USING exam_dossiers d, exam_sessions s
                    WHERE r."ExamDossierId" = d."Id" AND d."ExamSessionId" = s."Id"
                      AND s."SchoolYearId" = p_school_year_id AND r."SchoolId" = p_school_id;
                """, "Résultats d'examens")}}
                {{Step("""
                    DELETE FROM exam_dossiers d
                    USING exam_sessions s
                    WHERE d."ExamSessionId" = s."Id"
                      AND s."SchoolYearId" = p_school_year_id AND d."SchoolId" = p_school_id;
                """, "Dossiers de candidature aux examens")}}
                {{Step("""
                    DELETE FROM exam_sessions
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Sessions d'examens")}}
                {{Step("""
                    DELETE FROM student_mutation_certificates
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Certificats de mutation")}}
                {{Step("""
                    DELETE FROM terms
                    WHERE "SchoolYearId" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Trimestres")}}
                {{Step("""
                    DELETE FROM school_years
                    WHERE "Id" = p_school_year_id AND "SchoolId" = p_school_id;
                """, "Année scolaire")}}
                END;
                $$;
                """);

            // Même régime que reset_school_data : la fonction n'est appelable que par le rôle applicatif,
            // jamais par PUBLIC — c'est elle, et non un GRANT DELETE, qui porte l'opération.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION {Signature} FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION {Signature} TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la suppression d''une année scolaire en mode test sera inaccessible.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"DROP FUNCTION IF EXISTS {Signature};");
        }
    }
}
