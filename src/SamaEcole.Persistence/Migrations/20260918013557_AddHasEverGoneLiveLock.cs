using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Sépare le verrou de purge (« Zone de danger ») de l'affichage courant du mode réel/test.
    ///
    /// Jusqu'ici, <c>reset_school_data</c> ET <c>ResetSchoolDataCommandHandler</c> lisaient
    /// <c>schools."WentLiveAt"</c> pour interdire la purge — le même champ que
    /// <c>RevertToTestCommand</c> remet à <c>null</c>. Cette bascule n'était possible que sur les
    /// environnements jetables (drapeau <c>SAMA_RETOUR_MODE_TEST_AUTORISE</c>), donc l'écart restait
    /// sans conséquence. Ce drapeau disparaît : le Directeur peut désormais repasser en mode test à
    /// tout moment, sur tout environnement. Sans ce verrou distinct, ce retour aurait rouvert la
    /// purge d'un établissement dont les données sont pourtant devenues comptables (AGENTS.md
    /// règle #6) — l'invariant même que la fonction protège.
    ///
    /// <c>schools."HasEverGoneLive"</c> est posé UNE SEULE FOIS, au premier passage réel
    /// (GoLiveCommandHandler), et n'est JAMAIS effacé — y compris par RevertToTestCommand. C'est ce
    /// champ, et non <c>WentLiveAt</c>, que la fonction interroge désormais. Le backfill couvre les
    /// écoles déjà passées en mode réel avant cette migration : leur verrou s'applique
    /// rétroactivement, sans attendre un futur GoLiveCommand qui ne serait jamais rejoué.
    ///
    /// <c>Down()</c> restaure la fonction dans son état GuardResetSchoolDataAgainstLiveMode, à
    /// l'identique (garde sur WentLiveAt).
    /// </summary>
    public partial class AddHasEverGoneLiveLock : Migration
    {
        /// <summary>Corps commun aux deux sens : seul le bloc de gardes change.</summary>
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
                        ['ParentSummons',                'Convocations de parents'],
                        ['sms_messages',                'SMS envoyés'],

                        -- Caisse : sessions et décaissements, une fois les paiements partis.
                        ['cashier_sessions',            'Sessions de caisse'],
                        ['Disbursements',               'Décaissements'],

                        -- Examens officiels, certificats de mutation et prêts de matériel : chacun
                        -- porte une FK ON DELETE RESTRICT vers students (modules livrés après la
                        -- création de cette fonction). Ils DOIVENT partir avant l'élève, sinon la
                        -- purge échoue en 23503. Ordre enfant -> parent pour les examens.
                        ['exam_results',                'Résultats d''examens'],
                        ['exam_dossiers',                'Dossiers de candidature aux examens'],
                        ['exam_sessions',                'Sessions d''examens'],
                        ['student_mutation_certificates', 'Certificats de mutation'],
                        ['item_assignments',            'Affectations de matériel'],

                        -- Élèves, puis les compteurs qui repartent à zéro avec eux : sans cela, le
                        -- premier élève recréé porterait ELEV-2026-0043 et l'école garderait la
                        -- mémoire de ses essais.
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules']
                    ];
            """;

        /// <summary>Boucle de purge, identique dans les deux sens.</summary>
        private const string DeleteLoop = """
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
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEverGoneLive",
                table: "schools",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill : toute école déjà en mode réel à l'instant de cette migration a forcément
            // déjà des données comptables — le verrou doit s'appliquer rétroactivement.
            migrationBuilder.Sql(
                """UPDATE schools SET "HasEverGoneLive" = true WHERE "WentLiveAt" IS NOT NULL;""");

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

                    -- Verrou PERMANENT : une fois qu'une école est passée en mode réel une seule fois,
                    -- la purge est interdite pour toujours (AGENTS.md règle #6) — y compris après un
                    -- retour en mode test (RevertToTestCommand, disponible à tout moment), qui remet
                    -- WentLiveAt à null mais jamais HasEverGoneLive. Ce contrôle existe AUSSI dans
                    -- ResetSchoolDataCommandHandler ; le dédoubler ici aligne l'invariant d'immuabilité
                    -- comptable sur celui du tenant, déjà protégé des deux côtés (règle #2). Placé
                    -- APRÈS les gardes de tenant : un appelant sans tenant ne doit pas pouvoir déduire,
                    -- du message reçu, l'état d'une école qui ne le regarde pas.
                    SELECT s."HasEverGoneLive" INTO v_has_ever_gone_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_has_ever_gone_live THEN
                        RAISE EXCEPTION
                            'RESET_UNAVAILABLE_LIVE_MODE : l''établissement % est déjà passé en mode réel par le passé ; ses données sont comptables et ne peuvent plus être effacées, même après un retour en mode test.',
                            p_school_id
                            USING ERRCODE = 'raise_exception';
                    END IF;

                {{DeleteLoop}}
                END;
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // État GuardResetSchoolDataAgainstLiveMode : garde sur WentLiveAt.
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
                    v_went_live timestamptz;
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

                    SELECT s."WentLiveAt" INTO v_went_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_went_live IS NOT NULL THEN
                        RAISE EXCEPTION
                            'RESET_UNAVAILABLE_LIVE_MODE : l''établissement % est passé en mode réel le % ; ses données sont comptables et ne peuvent plus être effacées.',
                            p_school_id, v_went_live
                            USING ERRCODE = 'raise_exception';
                    END IF;

                {{DeleteLoop}}
                END;
                $$;
                """);

            migrationBuilder.DropColumn(
                name: "HasEverGoneLive",
                table: "schools");
        }
    }
}
