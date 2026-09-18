using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Élargit la « remise à neuf » du mode test (<c>reset_school_data</c>) de la seule SAISIE à la
    /// CONFIGURATION métier, arbitrage du Directeur du 15/09/2026 : après une phase d'essai, l'école
    /// doit repartir sans classes, matières, enseignants, barème ni inventaire fictifs.
    ///
    /// Périmètre ajouté : classes (et leurs niveaux, simple colonne), matières, enseignants et leurs
    /// affectations, emploi du temps, pointages, contrats et fiches de paie, déclarations fiscales,
    /// barème des frais (catégories, frais par classe, historique), inventaire (catégories, biens,
    /// mouvements de stock), et les COMPTES du personnel non-Directeur.
    ///
    /// Toujours CONSERVÉS : la fiche de l'établissement, ses réglages (formats, signatures, SMS), les
    /// comptes Directeur, l'abonnement, les années scolaires et trimestres, les mentions, les bâtiments
    /// et salles, le journal d'audit.
    ///
    /// COMPTES DU PERSONNEL — suppression physique, avec trois précautions :
    ///   1. le journal d'audit n'est PAS purgé : ses entrées sont DÉTACHÉES (<c>UserId</c> → NULL, d'où
    ///      la colonne rendue nullable ci-dessous) — l'action reste tracée, son auteur s'affiche
    ///      « Compte supprimé » ;
    ///   2. un compte rattaché AUSSI à un autre établissement (<c>user_schools</c>, groupe scolaire)
    ///      n'est jamais supprimé : la purge d'une école ne doit rien retirer à une autre, qui peut être
    ///      en mode réel. Seul son rattachement à CETTE école part ;
    ///   3. le Directeur (et tout Super Admin) n'est jamais concerné — il doit pouvoir se reconnecter.
    ///
    /// Les gardes (tenant, puis mode réel) sont inchangées : cette opération n'existe qu'en mode test.
    ///
    /// <c>get_global_audit_logs</c> passe en LEFT JOIN sur <c>users</c> : en INNER JOIN, les entrées
    /// détachées disparaîtraient silencieusement du journal de la console Super Admin.
    ///
    /// <c>Down()</c> restaure les deux fonctions à l'identique, puis remet la colonne NOT NULL — ce qui
    /// ÉCHOUE, volontairement, si des entrées ont déjà été détachées : inventer un auteur pour les
    /// satisfaire (l'échafaudage EF proposait l'UUID nul) violerait la clé étrangère et falsifierait
    /// le journal.
    /// </summary>
    public partial class ExtendResetSchoolDataToConfiguration : Migration
    {
        /// <summary>Tables purgées par clé SchoolId, dans l'ordre enfant → parent des clés étrangères.</summary>
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
                        ['exam_dossiers',               'Dossiers de candidature aux examens'],
                        ['exam_sessions',               'Sessions d''examens'],
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
                        ['fee_categories',              'Catégories de frais'],

                        -- Inventaire. stock_movements est append-only pour le rôle applicatif (SELECT,
                        -- INSERT) : cette fonction, propriétaire, est la SEULE voie qui l'efface.
                        ['stock_movements',             'Mouvements de stock'],
                        ['inventory_items',             'Équipements et stocks'],
                        ['inventory_categories',        'Catégories d''inventaire'],

                        -- Emploi du temps, pointages, paie : tout ce qui pend aux enseignants et aux
                        -- contrats (ScheduleSlots, employee_contracts, fiche_paies… : FK RESTRICT).
                        ['ScheduleSlots',               'Créneaux d''emploi du temps'],
                        ['TeacherAttendances',          'Pointages des enseignants'],
                        ['TeacherHourRecords',          'Relevés d''heures'],
                        ['fiche_paies',                 'Fiches de paie'],
                        ['employee_contract_histories', 'Historique des contrats'],
                        ['employee_contracts',          'Contrats du personnel'],
                        ['teacher_assignments',         'Affectations des enseignants'],
                        ['teacher_subjects',            'Matières enseignées'],
                        ['teachers',                    'Enseignants'],

                        -- Pédagogie : matières (auto-référencées, voir la mise à NULL du parent plus
                        -- haut), puis classes — le niveau n'est qu'une colonne de la classe.
                        ['subjects',                    'Matières'],
                        ['classrooms',                  'Classes et niveaux']
                    ];
            """;

        /// <summary>Gardes de la fonction, inchangées depuis GuardResetSchoolDataAgainstLiveMode.</summary>
        private const string Guards = """
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

                    -- Mode réel : la purge n'est plus une option, quelle que soit la porte d'entrée. Placé
                    -- APRÈS les gardes de tenant, pour ne rien divulguer de l'état d'une autre école.
                    SELECT s."WentLiveAt" INTO v_went_live FROM schools s WHERE s."Id" = p_school_id;

                    IF v_went_live IS NOT NULL THEN
                        RAISE EXCEPTION
                            'RESET_UNAVAILABLE_LIVE_MODE : l''établissement % est passé en mode réel le % ; ses données sont comptables et ne peuvent plus être effacées.',
                            p_school_id, v_went_live
                            USING ERRCODE = 'raise_exception';
                    END IF;
            """;

        /// <summary>Boucle de purge générique, identique à celle des migrations précédentes.</summary>
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
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "audit_logs",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

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
                    v_staff uuid[];
                BEGIN
                {{Guards}}

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

                    -- Le journal d'audit est CONSERVÉ : ses entrées sont détachées, pas effacées. Toutes
                    -- écoles confondues — la FK vers users l'exige, et v_staff n'a accès qu'à celle-ci.
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

            migrationBuilder.Sql(GlobalAuditLogsFunction(actorJoin: "LEFT JOIN", actorName: "COALESCE(u.\"FullName\", 'Compte supprimé')"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(GlobalAuditLogsFunction(actorJoin: "JOIN", actorName: "u.\"FullName\""));

            // État GuardResetSchoolDataAgainstLiveMode, à l'identique.
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
                        ['exam_dossiers',               'Dossiers de candidature aux examens'],
                        ['exam_sessions',               'Sessions d''examens'],
                        ['student_mutation_certificates', 'Certificats de mutation'],
                        ['item_assignments',            'Affectations de matériel'],
                        ['students',                    'Élèves'],
                        ['matricule_sequences',         'Compteurs de matricules']
                    ];
                    v_session_school uuid;
                    v_deleted integer;
                    v_index integer;
                    v_went_live timestamptz;
                BEGIN
                {{Guards}}

                {{DeleteLoop}}
                END;
                $$;
                """);

            // Échoue (23502) si la purge a déjà détaché des entrées : voir le résumé de la classe.
            migrationBuilder.AlterColumn<Guid>(
                name: "UserId",
                table: "audit_logs",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }

        /// <summary>
        /// Corps de <c>get_global_audit_logs</c> (migration ExtendGlobalAuditLogsFilters). Même signature
        /// et même type de retour : CREATE OR REPLACE suffit, propriétaire et GRANT sont conservés.
        /// </summary>
        private static string GlobalAuditLogsFunction(string actorJoin, string actorName) => $$"""
            CREATE OR REPLACE FUNCTION public.get_global_audit_logs(
                limit_val int,
                offset_val int,
                module_filter text DEFAULT NULL,
                success_filter boolean DEFAULT NULL,
                school_id_filter uuid DEFAULT NULL,
                date_from timestamptz DEFAULT NULL,
                date_to timestamptz DEFAULT NULL)
            RETURNS TABLE (
                "Id" uuid,
                "SchoolId" uuid,
                "SchoolName" text,
                "UserId" uuid,
                "ActorFullName" text,
                "Module" text,
                "Action" text,
                "Success" boolean,
                "FailureReason" text,
                "IpAddress" text,
                "OccurredAt" timestamptz,
                "TotalCount" integer)
            LANGUAGE sql
            SECURITY DEFINER
            SET search_path = public
            AS $$
                SELECT
                    a."Id", a."SchoolId", s."Name", a."UserId", {{actorName}},
                    a."Module", a."Action", a."Success", a."FailureReason", a."IpAddress", a."OccurredAt",
                    COUNT(*) OVER()::integer AS "TotalCount"
                FROM audit_logs a
                JOIN schools s ON s."Id" = a."SchoolId"
                {{actorJoin}} users u ON u."Id" = a."UserId"
                WHERE NOT a."IsDeleted"
                  AND (module_filter IS NULL OR a."Module" = module_filter)
                  AND (success_filter IS NULL OR a."Success" = success_filter)
                  AND (school_id_filter IS NULL OR a."SchoolId" = school_id_filter)
                  AND (date_from IS NULL OR a."OccurredAt" >= date_from)
                  AND (date_to IS NULL OR a."OccurredAt" <= date_to)
                ORDER BY a."OccurredAt" DESC
                LIMIT limit_val OFFSET offset_val;
            $$;
            """;
    }
}
