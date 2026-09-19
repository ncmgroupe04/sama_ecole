using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ajoute <c>class_journal_entries</c> (ticket JGK-P04, migration AddClassJournal) à
    /// <c>v_targets</c> — trois FK ON DELETE RESTRICT (Classroom, Subject, Teacher) que
    /// reset_school_data ignorait, exactement le bug documenté par
    /// <c>Every_Restrict_Foreign_Key_Into_A_Purged_Table_Must_Come_From_A_Purged_Table_Too</c>
    /// (voir FixResetSchoolDataMissingChildTables, qui a corrigé le même oubli pour Examens,
    /// Intégration étatique et Inventaire). Placée AVANT teachers/subjects/classrooms dans la
    /// boucle de purge, comme l'exige la clé étrangère.
    ///
    /// AGENTS.md règle « ne jamais modifier une migration déjà appliquée » : on ne touche pas
    /// FixResetSchoolDataRegressionFromLiveModeGuard, on la corrige par une nouvelle migration.
    /// </summary>
    public partial class AddClassJournalToResetSchoolData : Migration
    {
        /// <summary>Périmètre complet, identique à FixResetSchoolDataRegressionFromLiveModeGuard + class_journal_entries.</summary>
        private const string Targets = """
                    v_targets text[][] := ARRAY[
                        -- Finance : encaissements, échéanciers et mouvements de caisse.
                        ['payment_breakdowns',          'Ventilations de paiement'],
                        ['payments',                    'Paiements'],
                        ['debtor_reminder_batch_items', 'Relances de débiteurs (lignes)'],
                        ['debtor_reminder_batches',     'Relances de débiteurs (lots)'],
                        ['fee_installments',            'Échéances'],
                        ['fee_installment_plans',       'Échéanciers'],
                        ['FinancialCommitments',        'Engagements financiers'],

                        -- Inscriptions, et le détail figé des frais dus qu'elles portent.
                        ['enrollment_fee_lines',        'Lignes de frais d''inscription'],
                        ['enrollments',                 'Inscriptions'],

                        -- Vie scolaire rattachée aux élèves.
                        ['student_attendances',         'Présences des élèves'],
                        ['attendance_sheets',           'Fiches d''appel'],
                        ['grades',                      'Notes'],
                        ['report_card_remarks',         'Appréciations de bulletin'],
                        ['AbsenceJustifications',       'Justificatifs d''absence'],
                        ['DisciplineRecords',           'Sanctions disciplinaires'],
                        ['EarlyDepartures',             'Départs anticipés'],
                        ['LateArrivals',                'Retards'],
                        ['ParentSummons',               'Convocations de parents'],
                        ['sms_messages',                'SMS envoyés'],

                        -- Caisse : sessions et décaissements, une fois les paiements partis.
                        ['cashier_sessions',            'Sessions de caisse'],
                        ['Disbursements',               'Décaissements'],

                        -- Examens officiels, certificats de mutation et prêts de matériel : FK RESTRICT
                        -- vers students (et item_assignments vers inventory_items / teachers / users).
                        ['exam_results',                'Résultats d''examens'],
                        ['exam_dossiers',                'Dossiers de candidature aux examens'],
                        ['exam_sessions',                'Sessions d''examens'],
                        ['student_mutation_certificates', 'Certificats de mutation'],
                        ['item_assignments',            'Affectations de matériel'],

                        -- Élèves, puis les compteurs de matricules (élèves ET enseignants) qui repartent
                        -- à zéro avec eux.
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules'],

                        -- ---- Périmètre ajouté le 15/09/2026 : la configuration métier. ----

                        -- Trésorerie & fiscalité.
                        ['taxe_declarations',           'Déclarations fiscales (TVA)'],

                        -- Barème des frais de scolarité. payment_breakdowns (FK vers fee_categories) et
                        -- fee_change_history (FK vers class_fees) sont déjà partis.
                        ['fee_change_history',          'Historique du barème des frais'],
                        ['class_fees',                  'Frais de scolarité par classe'],
                        ['fee_categories',               'Catégories de frais'],

                        -- Inventaire. stock_movements est append-only pour le rôle applicatif (SELECT,
                        -- INSERT) : cette fonction, propriétaire, est la SEULE voie qui l'efface.
                        ['stock_movements',              'Mouvements de stock'],
                        ['inventory_items',              'Équipements et stocks'],
                        ['inventory_categories',         'Catégories d''inventaire'],

                        -- Cahier de texte (JGK-P04) : FK RESTRICT vers classrooms, subjects ET
                        -- teachers — doit donc être purgé avant les trois.
                        ['class_journal_entries',        'Entrées de cahier de texte'],

                        -- Emploi du temps, pointages, paie : tout ce qui pend aux enseignants et aux
                        -- contrats (ScheduleSlots, employee_contracts, fiche_paies… : FK RESTRICT).
                        ['ScheduleSlots',                'Créneaux d''emploi du temps'],
                        ['TeacherAttendances',           'Pointages des enseignants'],
                        ['TeacherHourRecords',           'Relevés d''heures'],
                        ['fiche_paies',                  'Fiches de paie'],
                        ['employee_contract_histories',  'Historique des contrats'],
                        ['employee_contracts',           'Contrats du personnel'],
                        ['teacher_assignments',          'Affectations des enseignants'],
                        ['teacher_subjects',             'Matières enseignées'],
                        ['teachers',                     'Enseignants'],

                        -- Pédagogie : matières (auto-référencées, voir la mise à NULL du parent plus
                        -- haut), puis classes — le niveau n'est qu'une colonne de la classe.
                        ['subjects',                     'Matières'],
                        ['classrooms',                   'Classes et niveaux']
                    ];
            """;

        /// <summary>Boucle de purge générique, identique aux migrations précédentes.</summary>
        private const string DeleteLoop = """
                    FOR v_index IN 1 .. array_length(v_targets, 1) LOOP
                        -- %I quote l'identifiant : les tables en CamelCase exigent des guillemets.
                        EXECUTE format('DELETE FROM %I WHERE "SchoolId" = $1', v_targets[v_index][1])
                            USING p_school_id;

                        GET DIAGNOSTICS v_deleted = ROW_COUNT;

                        label := v_targets[v_index][2];
                        rows_deleted := v_deleted;
                        RETURN NEXT;
                    END LOOP;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                CREATE OR REPLACE FUNCTION reset_school_data(p_school_id uuid)
                RETURNS TABLE (label text, rows_deleted integer)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    -- Chaque ligne : { table physique, libellé affiché au Directeur }.
                {{Targets}}
                    v_session_school uuid;
                    v_deleted integer;
                    v_index integer;
                    v_has_ever_gone_live boolean;
                    v_staff uuid[];
                BEGIN
                    v_session_school := NULLIF(current_setting('app.current_school_id', true), '')::uuid;

                    -- Aucune session tenant : rien à purger, et surtout pas « tout ». Échec en FERMETURE.
                    IF v_session_school IS NULL THEN
                        RAISE EXCEPTION 'reset_school_data : aucun établissement dans la session (app.current_school_id absent).'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- LA garde qui remplace la RLS, dont un SECURITY DEFINER est exempté.
                    IF v_session_school <> p_school_id THEN
                        RAISE EXCEPTION 'reset_school_data : l''établissement visé (%) n''est pas celui de la session (%).',
                            p_school_id, v_session_school
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- Verrou PERMANENT (AddHasEverGoneLiveLock) : une fois qu'une école est passée en mode
                    -- réel une seule fois, la purge est interdite pour toujours, même après un retour en
                    -- mode test (RevertToTestCommand, disponible à tout moment) qui remet WentLiveAt à null
                    -- mais jamais HasEverGoneLive. Placé APRÈS les gardes de tenant.
                    SELECT s."HasEverGoneLive" INTO v_has_ever_gone_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_has_ever_gone_live THEN
                        RAISE EXCEPTION
                            'RESET_UNAVAILABLE_LIVE_MODE : l''établissement % est déjà passé en mode réel par le passé ; ses données sont comptables et ne peuvent plus être effacées, même après un retour en mode test.',
                            p_school_id
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    -- subjects.ParentSubjectId est une FK ON DELETE RESTRICT vers subjects elle-même :
                    -- RESTRICT n'est pas différable, on ne parie donc pas sur l'ordre interne du DELETE.
                    UPDATE subjects SET "ParentSubjectId" = NULL
                    WHERE "SchoolId" = p_school_id AND "ParentSubjectId" IS NOT NULL;

                {{DeleteLoop}}

                    -- ---- Comptes du personnel (tout rôle sauf Directeur et Super Admin). ----
                    --
                    -- Seuls les comptes dont CETTE école est l'établissement de rattachement ET qui n'ont
                    -- accès à aucune autre (user_schools) : supprimer un compte partagé par un groupe
                    -- scolaire retirerait un utilisateur à une école qui n'a rien demandé.
                    SELECT COALESCE(array_agg(u."Id"), '{}') INTO v_staff
                    FROM users u
                    WHERE u."SchoolId" = p_school_id
                      AND u."Role" NOT IN ('Directeur', 'SuperAdmin')
                      AND NOT EXISTS (
                          SELECT 1 FROM user_schools us
                          WHERE us."UserId" = u."Id" AND us."SchoolId" <> p_school_id);

                    -- Rattachements À cette école de membres du personnel venus d'un autre établissement :
                    -- seul le lien part, le compte appartient à l'autre école.
                    DELETE FROM user_schools us
                    USING users u
                    WHERE us."UserId" = u."Id"
                      AND us."SchoolId" = p_school_id
                      AND u."Role" NOT IN ('Directeur', 'SuperAdmin')
                      AND NOT (u."Id" = ANY (v_staff));
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Accès de personnel d''autres établissements'; rows_deleted := v_deleted; RETURN NEXT;

                    -- Le journal d'audit est CONSERVÉ : ses entrées sont détachées, pas effacées.
                    UPDATE audit_logs SET "UserId" = NULL WHERE "UserId" = ANY (v_staff);

                    DELETE FROM refresh_tokens WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Sessions de connexion du personnel'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM password_reset_tokens WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Liens de réinitialisation de mot de passe'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM user_status_history WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Historique de statut des comptes'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM user_schools WHERE "UserId" = ANY (v_staff);

                    DELETE FROM users WHERE "Id" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Comptes du personnel'; rows_deleted := v_deleted; RETURN NEXT;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restaure le corps exact de FixResetSchoolDataRegressionFromLiveModeGuard (sans
            // class_journal_entries) — la migration précédente reste, elle, inchangée.
            migrationBuilder.Sql($$"""
                CREATE OR REPLACE FUNCTION reset_school_data(p_school_id uuid)
                RETURNS TABLE (label text, rows_deleted integer)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_targets text[][] := ARRAY[
                        ['payment_breakdowns',          'Ventilations de paiement'],
                        ['payments',                    'Paiements'],
                        ['debtor_reminder_batch_items', 'Relances de débiteurs (lignes)'],
                        ['debtor_reminder_batches',     'Relances de débiteurs (lots)'],
                        ['fee_installments',            'Échéances'],
                        ['fee_installment_plans',       'Échéanciers'],
                        ['FinancialCommitments',        'Engagements financiers'],
                        ['enrollment_fee_lines',        'Lignes de frais d''inscription'],
                        ['enrollments',                 'Inscriptions'],
                        ['student_attendances',         'Présences des élèves'],
                        ['attendance_sheets',           'Fiches d''appel'],
                        ['grades',                      'Notes'],
                        ['report_card_remarks',         'Appréciations de bulletin'],
                        ['AbsenceJustifications',       'Justificatifs d''absence'],
                        ['DisciplineRecords',           'Sanctions disciplinaires'],
                        ['EarlyDepartures',             'Départs anticipés'],
                        ['LateArrivals',                'Retards'],
                        ['ParentSummons',               'Convocations de parents'],
                        ['sms_messages',                'SMS envoyés'],
                        ['cashier_sessions',            'Sessions de caisse'],
                        ['Disbursements',               'Décaissements'],
                        ['exam_results',                'Résultats d''examens'],
                        ['exam_dossiers',                'Dossiers de candidature aux examens'],
                        ['exam_sessions',                'Sessions d''examens'],
                        ['student_mutation_certificates', 'Certificats de mutation'],
                        ['item_assignments',            'Affectations de matériel'],
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules'],
                        ['taxe_declarations',           'Déclarations fiscales (TVA)'],
                        ['fee_change_history',          'Historique du barème des frais'],
                        ['class_fees',                  'Frais de scolarité par classe'],
                        ['fee_categories',               'Catégories de frais'],
                        ['stock_movements',              'Mouvements de stock'],
                        ['inventory_items',              'Équipements et stocks'],
                        ['inventory_categories',         'Catégories d''inventaire'],
                        ['ScheduleSlots',                'Créneaux d''emploi du temps'],
                        ['TeacherAttendances',           'Pointages des enseignants'],
                        ['TeacherHourRecords',           'Relevés d''heures'],
                        ['fiche_paies',                  'Fiches de paie'],
                        ['employee_contract_histories',  'Historique des contrats'],
                        ['employee_contracts',           'Contrats du personnel'],
                        ['teacher_assignments',          'Affectations des enseignants'],
                        ['teacher_subjects',             'Matières enseignées'],
                        ['teachers',                     'Enseignants'],
                        ['subjects',                     'Matières'],
                        ['classrooms',                   'Classes et niveaux']
                    ];
                    v_session_school uuid;
                    v_deleted integer;
                    v_index integer;
                    v_has_ever_gone_live boolean;
                    v_staff uuid[];
                BEGIN
                    v_session_school := NULLIF(current_setting('app.current_school_id', true), '')::uuid;

                    IF v_session_school IS NULL THEN
                        RAISE EXCEPTION 'reset_school_data : aucun établissement dans la session (app.current_school_id absent).'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    IF v_session_school <> p_school_id THEN
                        RAISE EXCEPTION 'reset_school_data : l''établissement visé (%) n''est pas celui de la session (%).',
                            p_school_id, v_session_school
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    SELECT s."HasEverGoneLive" INTO v_has_ever_gone_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_has_ever_gone_live THEN
                        RAISE EXCEPTION
                            'RESET_UNAVAILABLE_LIVE_MODE : l''établissement % est déjà passé en mode réel par le passé ; ses données sont comptables et ne peuvent plus être effacées, même après un retour en mode test.',
                            p_school_id
                            USING ERRCODE = 'raise_exception';
                    END IF;

                    UPDATE subjects SET "ParentSubjectId" = NULL
                    WHERE "SchoolId" = p_school_id AND "ParentSubjectId" IS NOT NULL;

                    FOR v_index IN 1 .. array_length(v_targets, 1) LOOP
                        EXECUTE format('DELETE FROM %I WHERE "SchoolId" = $1', v_targets[v_index][1])
                            USING p_school_id;

                        GET DIAGNOSTICS v_deleted = ROW_COUNT;

                        label := v_targets[v_index][2];
                        rows_deleted := v_deleted;
                        RETURN NEXT;
                    END LOOP;

                    SELECT COALESCE(array_agg(u."Id"), '{}') INTO v_staff
                    FROM users u
                    WHERE u."SchoolId" = p_school_id
                      AND u."Role" NOT IN ('Directeur', 'SuperAdmin')
                      AND NOT EXISTS (
                          SELECT 1 FROM user_schools us
                          WHERE us."UserId" = u."Id" AND us."SchoolId" <> p_school_id);

                    DELETE FROM user_schools us
                    USING users u
                    WHERE us."UserId" = u."Id"
                      AND us."SchoolId" = p_school_id
                      AND u."Role" NOT IN ('Directeur', 'SuperAdmin')
                      AND NOT (u."Id" = ANY (v_staff));
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Accès de personnel d''autres établissements'; rows_deleted := v_deleted; RETURN NEXT;

                    UPDATE audit_logs SET "UserId" = NULL WHERE "UserId" = ANY (v_staff);

                    DELETE FROM refresh_tokens WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Sessions de connexion du personnel'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM password_reset_tokens WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Liens de réinitialisation de mot de passe'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM user_status_history WHERE "UserId" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Historique de statut des comptes'; rows_deleted := v_deleted; RETURN NEXT;

                    DELETE FROM user_schools WHERE "UserId" = ANY (v_staff);

                    DELETE FROM users WHERE "Id" = ANY (v_staff);
                    GET DIAGNOSTICS v_deleted = ROW_COUNT;
                    label := 'Comptes du personnel'; rows_deleted := v_deleted; RETURN NEXT;
                END;
                $$;
                """);
        }
    }
}
