using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Porte le contrôle « mode réel » DANS la fonction <c>reset_school_data</c>, à côté du contrôle
    /// de tenant qui s'y trouvait déjà.
    ///
    /// Pourquoi : jusqu'ici, la barrière du tenant était DOUBLE (Global Query Filter EF + comparaison
    /// à <c>app.current_school_id</c> dans la fonction), conformément à la règle #2 d'AGENTS.md
    /// — « les deux, jamais un seul » —, tandis que la barrière du MODE RÉEL n'existait qu'en un seul
    /// exemplaire, dans <c>ResetSchoolDataCommandHandler</c>. Or c'est elle qui protège l'invariant le
    /// plus lourd du produit : une fois l'établissement passé en exploitation réelle, ses données sont
    /// comptables et la règle #6 les déclare inaltérables.
    ///
    /// Aujourd'hui aucun chemin ne contourne le Handler (<c>ResetSchoolDataService</c> est son seul
    /// appelant). Cette migration ne corrige donc pas une faille exploitable : elle supprime une
    /// ASYMÉTRIE. Le jour où un second appelant apparaîtra — une commande d'administration, une tâche
    /// de reprise, un script — il trouvera la porte fermée par la base, sans avoir à se souvenir de
    /// rejouer le contrôle.
    ///
    /// Ordre des gardes, volontaire : tenant d'abord (qui es-tu, et de quelle école ?), état métier
    /// ensuite (cette école a-t-elle le droit d'être purgée ?). Un appelant sans tenant ne doit jamais
    /// apprendre, par la différence de message, si telle école est en mode réel ou non.
    ///
    /// Code d'erreur : <c>raise_exception</c> (P0001), l'idiome plpgsql d'une violation de règle
    /// métier, avec le jeton stable <c>RESET_UNAVAILABLE_LIVE_MODE</c> dans le message — le MÊME
    /// identifiant que celui déjà renvoyé au client par <c>BusinessRuleException</c>. Ce garde n'est
    /// pas censé se déclencher en exploitation normale : le Handler refuse bien avant. S'il se
    /// déclenche, c'est le signe d'un contournement ou d'une régression, et la remontée en 500 avec
    /// trace au journal est la bonne réponse — la masquer derrière un 409 propre reviendrait à
    /// dissimuler un défaut de code.
    ///
    /// <c>Down()</c> restaure la fonction dans son état FixResetSchoolDataMissingChildTables, à
    /// l'identique.
    /// </summary>
    public partial class GuardResetSchoolDataAgainstLiveMode : Migration
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

                    -- Mode réel : la purge n'est plus une option, quelle que soit la porte d'entrée.
                    -- Ce contrôle existe AUSSI dans ResetSchoolDataCommandHandler ; le dédoubler ici
                    -- aligne l'invariant d'immuabilité comptable (AGENTS.md règle #6) sur celui du
                    -- tenant, déjà protégé des deux côtés (règle #2, « les deux, jamais un seul »).
                    -- Placé APRÈS les gardes de tenant : un appelant sans tenant ne doit pas pouvoir
                    -- déduire, du message reçu, l'état d'une école qui ne le regarde pas.
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // État FixResetSchoolDataMissingChildTables : sans le contrôle du mode réel.
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

                {{DeleteLoop}}
                END;
                $$;
                """);
        }
    }
}
