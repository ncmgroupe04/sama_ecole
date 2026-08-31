using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Rattrape <c>reset_school_data</c> (migration AddSchoolDataReset) sur les modules livrés APRÈS
    /// elle. Sa liste de tables à purger était figée au 22/08 ; depuis, trois modules ont ajouté une
    /// clé étrangère <c>ON DELETE RESTRICT</c> vers <c>students</c> sans jamais l'inscrire dans la
    /// fonction :
    ///
    ///   * <c>item_assignments</c>            — prêts / affectations de matériel (AddInventoryModule) ;
    ///   * <c>exam_dossiers</c>               — dossiers de candidature aux examens (AddExamsModule) ;
    ///   * <c>student_mutation_certificates</c> — certificats de mutation (AddStateIntegrationModule).
    ///
    /// Résultat : dès qu'un établissement possède ne serait-ce qu'une de ces lignes, la « Zone de
    /// danger » atteignait <c>DELETE FROM students</c> et PostgreSQL levait une violation de clé
    /// étrangère (SQLSTATE 23503). Non mappée par ExceptionHandlingMiddleware, elle ressortait en
    /// HTTP 500 « Une erreur inattendue s'est produite » — et, l'appel étant un ordre SQL unique,
    /// toute la purge était annulée : l'écran restait donc inutilisable pour ces écoles.
    ///
    /// Le commentaire d'origine l'avait prévu mot pour mot : « Toute table ajoutée demain qui
    /// référencera l'une de celles-ci devra être insérée AVANT sa cible ». C'est ce que fait cette
    /// migration, par un <c>CREATE OR REPLACE</c> (jamais de modification d'une migration déjà
    /// appliquée) : cinq lignes de plus dans <c>v_targets</c>, placées avant <c>students</c>, dans
    /// l'ordre enfant → parent (<c>exam_results</c> avant <c>exam_dossiers</c> avant
    /// <c>exam_sessions</c>). La signature, la garde tenant, <c>SECURITY DEFINER</c> et
    /// <c>search_path</c> sont rigoureusement identiques — seul le contenu du tableau change.
    ///
    /// Aucun GRANT à ajouter : la fonction s'exécute avec les droits du propriétaire des tables, qui a
    /// le DELETE partout (y compris sur <c>stock_movements</c>, pourtant append-only pour le rôle
    /// applicatif). <c>inventory_items</c>, <c>stock_movements</c> et <c>inventory_categories</c> ne
    /// sont PAS purgés : c'est du patrimoine, pas une donnée d'essai rattachée à un élève, et rien
    /// n'y fait obstacle à la suppression des élèves.
    ///
    /// <c>Down()</c> restaure la fonction dans son état AddSchoolDataReset, à l'identique.
    /// </summary>
    public partial class FixResetSchoolDataMissingChildTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION reset_school_data(p_school_id uuid)
                RETURNS TABLE (label text, rows_deleted integer)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    -- Chaque ligne : { table physique, libellé affiché au Directeur }.
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

                        -- Examens officiels, certificats de mutation et prêts de matériel : chacun
                        -- porte une FK ON DELETE RESTRICT vers students (modules livrés après la
                        -- création de cette fonction). Ils DOIVENT partir avant l'élève, sinon la
                        -- purge échoue en 23503. Ordre enfant -> parent pour les examens.
                        ['exam_results',                'Résultats d''examens'],
                        ['exam_dossiers',               'Dossiers de candidature aux examens'],
                        ['exam_sessions',               'Sessions d''examens'],
                        ['student_mutation_certificates', 'Certificats de mutation'],
                        ['item_assignments',            'Affectations de matériel'],

                        -- Élèves, puis les compteurs qui repartent à zéro avec eux : sans cela, le
                        -- premier élève recréé porterait ELEV-2026-0043 et l'école garderait la
                        -- mémoire de ses essais.
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules']
                    ];
                    v_session_school uuid;
                    v_deleted integer;
                    v_index integer;
                BEGIN
                    v_session_school := NULLIF(current_setting('app.current_school_id', true), '')::uuid;

                    -- Aucune session tenant (tâche de fond, appel non authentifié) : rien à purger, et
                    -- surtout pas « tout ». La fonction échoue en FERMETURE, comme les policies RLS.
                    IF v_session_school IS NULL THEN
                        RAISE EXCEPTION 'reset_school_data : aucun établissement dans la session (app.current_school_id absent).'
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    -- LA garde qui remplace la RLS, dont un SECURITY DEFINER est exempté : l'école
                    -- visée doit être EXACTEMENT celle du JWT de la session. Un Directeur ne peut donc
                    -- pas purger l'établissement d'un confrère, même en forgeant l'appel.
                    IF v_session_school <> p_school_id THEN
                        RAISE EXCEPTION 'reset_school_data : l''établissement visé (%) n''est pas celui de la session (%).',
                            p_school_id, v_session_school
                            USING ERRCODE = 'insufficient_privilege';
                    END IF;

                    FOR v_index IN 1 .. array_length(v_targets, 1) LOOP
                        -- %I quote l'identifiant : les tables en CamelCase (Disbursements,
                        -- ParentSummons…) exigent des guillemets, les autres non.
                        EXECUTE format('DELETE FROM %I WHERE "SchoolId" = $1', v_targets[v_index][1])
                            USING p_school_id;

                        GET DIAGNOSTICS v_deleted = ROW_COUNT;

                        label := v_targets[v_index][2];
                        rows_deleted := v_deleted;
                        RETURN NEXT;
                    END LOOP;
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // État AddSchoolDataReset : sans les cinq tables des modules Examens, Intégration étatique
            // et Inventaire.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION reset_school_data(p_school_id uuid)
                RETURNS TABLE (label text, rows_deleted integer)
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    -- Chaque ligne : { table physique, libellé affiché au Directeur }.
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

                        -- Élèves, puis les compteurs qui repartent à zéro avec eux : sans cela, le
                        -- premier élève recréé porterait ELEV-2026-0043 et l'école garderait la
                        -- mémoire de ses essais.
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules']
                    ];
                    v_session_school uuid;
                    v_deleted integer;
                    v_index integer;
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

                    FOR v_index IN 1 .. array_length(v_targets, 1) LOOP
                        EXECUTE format('DELETE FROM %I WHERE "SchoolId" = $1', v_targets[v_index][1])
                            USING p_school_id;

                        GET DIAGNOSTICS v_deleted = ROW_COUNT;

                        label := v_targets[v_index][2];
                        rows_deleted := v_deleted;
                        RETURN NEXT;
                    END LOOP;
                END;
                $$;
                """);
        }
    }
}
