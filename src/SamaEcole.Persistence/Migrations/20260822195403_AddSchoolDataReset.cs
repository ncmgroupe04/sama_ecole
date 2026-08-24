using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// « Zone de danger » de l'écran Paramètres — le Directeur remet son établissement à neuf après
    /// une phase d'essai.
    ///
    /// LE PROBLÈME : le rôle applicatif n'a délibérément AUCUN droit de DELETE sur les tables métier.
    /// Chaque migration lui accorde « SELECT, INSERT, UPDATE — jamais DELETE » précisément pour que la
    /// règle #6 d'AGENTS.md (aucune suppression physique) soit tenue par PostgreSQL et pas seulement
    /// par la discipline du code C#. Un DELETE émis par l'application est donc refusé en 42501, et
    /// c'est très bien ainsi.
    ///
    /// LA SOLUTION, et pourquoi elle ne rouvre pas la porte : plutôt que d'accorder le DELETE à
    /// l'application — ce qui affaiblirait la garantie pour TOUT le produit, à jamais, au bénéfice
    /// d'un seul écran — une fonction SECURITY DEFINER dont le périmètre est figé ici. Même mécanisme
    /// que provision_school_director (JGK-B01) ou get_global_audit_logs : l'application ne gagne pas
    /// un droit, elle gagne UNE opération, dont la liste des tables et l'ordre de suppression sont
    /// écrits dans la base et non dans le code appelant.
    ///
    /// La contrepartie d'un SECURITY DEFINER est qu'il s'exécute avec les droits du PROPRIÉTAIRE des
    /// tables — que PostgreSQL exempte de RLS. La barrière tenant disparaîtrait donc… si la fonction
    /// ne la reconstituait pas elle-même : elle REFUSE d'agir si l'établissement visé n'est pas
    /// exactement celui de la session (app.current_school_id, positionné depuis le seul claim JWT par
    /// TenantConnectionInterceptor). Un appelant qui passerait l'identifiant d'une autre école est
    /// rejeté par la base, pas par une politesse applicative.
    ///
    /// CE QUI EST CONSERVÉ, et c'est le cœur de la fonctionnalité : les comptes utilisateurs (le
    /// Directeur doit pouvoir se reconnecter), la fiche et les réglages de l'école, les années
    /// scolaires et leurs trimestres, les classes, matières, mentions, enseignants, bâtiments, salles,
    /// le barème des frais et son historique, l'abonnement, et le journal d'audit — append-only
    /// (JGK-H01), et c'est justement lui qui gardera la trace de cette purge.
    ///
    /// `search_path` figé sur public, comme les autres fonctions SECURITY DEFINER du projet : sinon un
    /// rôle capable de créer un schéma pourrait y placer une fausse table et détourner une exécution
    /// qui tourne avec les droits du propriétaire.
    /// </summary>
    public partial class AddSchoolDataReset : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // L'ORDRE du tableau suit les clés étrangères, de l'enfant vers le parent. Toute table
            // ajoutée demain qui référencera l'une de celles-ci devra être insérée AVANT sa cible :
            // sinon la purge échoue franchement sur une violation de clé étrangère — ce qui est le
            // comportement voulu, la transaction annulant alors l'ensemble plutôt que de laisser
            // l'établissement à moitié vidé.
            migrationBuilder.Sql("""
                CREATE FUNCTION reset_school_data(p_school_id uuid)
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

            // Le rôle applicatif gagne le droit d'APPELER cette fonction, et rien d'autre : il reste
            // dépourvu de DELETE sur chacune des tables ci-dessus.
            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'GRANT EXECUTE ON FUNCTION reset_school_data(uuid) TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS reset_school_data(uuid);");
        }
    }
}
