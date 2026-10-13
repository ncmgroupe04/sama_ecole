using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Étape « Lock &amp; Upgrade » : demandes de changement d'abonnement (<c>subscription_change_requests</c>, table tenant
    /// sous RLS), drapeau de facturation <c>HasBoarding</c> et date de premier dépassement de quota
    /// <c>OverQuotaSince</c> sur <c>tenant_subscriptions</c>, remplacement de provision_configured_tenant_subscription
    /// (qui reçoit désormais HasBoarding) et trois fonctions SECURITY DEFINER pour le Super Admin, qui n'a aucun SchoolId de
    /// session : lister les demandes, en décider une, lister les écoles en dépassement.
    /// </summary>
    public partial class AddSubscriptionChangeRequests : Migration
    {
        private const string Table = "subscription_change_requests";
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasBoarding",
                table: "tenant_subscriptions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverQuotaSince",
                table: "tenant_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_change_requests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsDowngrade = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentProfile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrentTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CurrentCycles = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    CurrentHasBoarding = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentIsPedagogyEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentIsFinanceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentIsInternatEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentIsCoranModuleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetProfile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetEstablishmentProfile = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TargetTier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetMaxStudentLimit = table.Column<int>(type: "integer", nullable: false),
                    TargetSoftQuotaLimit = table.Column<int>(type: "integer", nullable: false),
                    TargetCycles = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    TargetHasBoarding = table.Column<bool>(type: "boolean", nullable: false),
                    TargetIsPedagogyEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetIsFinanceEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetIsInternatEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    TargetIsCoranModuleEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    CurrentMonthlyXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    CurrentAnnualXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    TargetMonthlyXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    TargetAnnualXof = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    RequestedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    DirectorNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReviewNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "text", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    UpdatedBy = table.Column<string>(type: "text", nullable: true),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DeletedBy = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_change_requests", x => x.Id);
                    table.CheckConstraint("CK_subscription_change_requests_target_cycles_not_empty", "\"TargetCycles\" <> ''");
                    table.CheckConstraint("CK_subscription_change_requests_target_max_positive", "\"TargetMaxStudentLimit\" > 0");
                    table.CheckConstraint("CK_subscription_change_requests_target_soft_gte_max", "\"TargetSoftQuotaLimit\" >= \"TargetMaxStudentLimit\"");
                    table.ForeignKey(
                        name: "FK_subscription_change_requests_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "UX_subscription_change_requests_pending_school",
                table: "subscription_change_requests",
                column: "SchoolId",
                unique: true,
                filter: "\"Status\" = 'Pending' AND NOT \"IsDeleted\"");

            // --- Isolation multi-tenant (AGENTS.md règle #2) ---
            // EF ne génère jamais les policies RLS : elles s'écrivent à la main, et RlsCoverageTests échoue si elles
            // manquent. Les demandes d'une école ne sont pas des données scolaires : « Réinitialiser l'école » ne les purge pas.
            migrationBuilder.Sql($"ALTER TABLE \"{Table}\" ENABLE ROW LEVEL SECURITY;");

            migrationBuilder.Sql($$"""
                CREATE POLICY {{Table}}_tenant_isolation ON "{{Table}}"
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Aucun DELETE : le soft delete n'émet jamais de SQL DELETE (règle #6).
            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON "{{Table}}" TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : l''application ne pourra pas lire la table {{Table}}.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

            // --- Reprise des écoles existantes : les droits couvrent au moins l'usage actuel ---
            // Les modules de la souscription sont désormais des DROITS (activer un module non souscrit = demande de
            // changement). Une école qui a déjà activé un module dans ses réglages (l'Internat, typiquement) ne doit pas se
            // voir refuser demain ce qu'elle utilise aujourd'hui : ses droits sont élargis à ses réglages actuels, jamais réduits.
            migrationBuilder.Sql("""
                UPDATE tenant_subscriptions t
                   SET "IsPedagogyEnabled" = t."IsPedagogyEnabled" OR s."IsPedagogyEnabled",
                       "IsFinanceEnabled" = t."IsFinanceEnabled" OR s."IsFinanceEnabled",
                       "IsInternatEnabled" = t."IsInternatEnabled" OR s."IsInternatEnabled",
                       "IsCoranModuleEnabled" = t."IsCoranModuleEnabled" OR s."IsCoranModuleEnabled"
                  FROM school_settings s
                 WHERE s."SchoolId" = t."SchoolId" AND NOT s."IsDeleted" AND NOT t."IsDeleted";
                """);

            // --- Provisionnement configuré : ajoute le drapeau de facturation HasBoarding ---
            // La signature change (un paramètre de plus) : l'ancienne fonction (migration
            // AddRegistrationOfferAndConfiguredProvisioning) est remplacée, pas surchargée, pour qu'aucun appelant ne puisse
            // créer une souscription sans dire si l'école a un internat.
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text);
                """);

            migrationBuilder.Sql("""
                CREATE FUNCTION provision_configured_tenant_subscription(
                    p_school_id uuid,
                    p_profile_type text,
                    p_establishment_profile text,
                    p_tier text,
                    p_max_students integer,
                    p_soft_limit integer,
                    p_pedagogy boolean,
                    p_finance boolean,
                    p_internat boolean,
                    p_coran boolean,
                    p_managed_cycles text,
                    p_has_boarding boolean)
                RETURNS uuid
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_id uuid;
                    v_settings_updated integer;
                BEGIN
                    INSERT INTO tenant_subscriptions (
                        "Id", "SchoolId", "ProfileType", "StudentQuotaTier", "MaxStudentLimit", "SoftQuotaLimit", "Status",
                        "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled", "IsCoranModuleEnabled", "HasBoarding",
                        "CreatedAt", "IsDeleted")
                    SELECT gen_random_uuid(), p_school_id, p_profile_type, p_tier, p_max_students, p_soft_limit, 'Active',
                           p_pedagogy, p_finance, p_internat, p_coran, p_has_boarding, NOW(), FALSE
                    -- LA garde : amorçage d'une souscription VIERGE uniquement.
                    WHERE NOT EXISTS (
                        SELECT 1 FROM tenant_subscriptions t WHERE t."SchoolId" = p_school_id AND NOT t."IsDeleted"
                    )
                    RETURNING "Id" INTO v_id;

                    IF v_id IS NULL THEN
                        RETURN NULL;
                    END IF;

                    -- Les réglages d'une école neuve n'ont encore aucun profil (provision_school_director les crée
                    -- avec NULL) : c'est cette condition qui interdit de reconfigurer une école en exploitation.
                    UPDATE school_settings
                       SET "ProfileEtablissement" = p_establishment_profile,
                           "IsPedagogyEnabled" = p_pedagogy,
                           "IsFinanceEnabled" = p_finance,
                           "IsInternatEnabled" = p_internat,
                           "IsCoranModuleEnabled" = p_coran,
                           "ManagedCycles" = p_managed_cycles,
                           "UpdatedAt" = NOW()
                     WHERE "SchoolId" = p_school_id AND NOT "IsDeleted" AND "ProfileEtablissement" IS NULL;

                    GET DIAGNOSTICS v_settings_updated = ROW_COUNT;

                    -- Souscription Active sans réglages alignés = sidebar en désaccord avec la souscription : on
                    -- annule plutôt que de laisser un état à moitié configuré (la transaction de l'approbation saute).
                    IF v_settings_updated = 0 THEN
                        RAISE EXCEPTION 'Réglages introuvables ou déjà configurés pour l''école %.', p_school_id;
                    END IF;

                    RETURN v_id;
                END
                $$;
                """);

            // --- Lectures et décision du Super Admin (sans SchoolId de session : RLS) ---
            // Même raison que get_tenant_subscription / update_tenant_subscription : la table est sous RLS et le Super
            // Admin n'a aucun tenant à présenter. SECURITY DEFINER, `search_path` figé, EXECUTE accordé au seul rôle
            // applicatif — et chaque fonction ne fait QUE ce que l'écran du Super Admin demande.

            // Liste les demandes (toutes écoles), filtrée par statut et/ou identifiant ; la plus récente d'abord.
            migrationBuilder.Sql("""
                CREATE FUNCTION list_subscription_change_requests(p_status text, p_id uuid)
                RETURNS SETOF subscription_change_requests
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT *
                      FROM subscription_change_requests
                     WHERE NOT "IsDeleted"
                       AND (p_status IS NULL OR "Status" = p_status)
                       AND (p_id IS NULL OR "Id" = p_id)
                     ORDER BY "CreatedAt" DESC;
                $$;
                """);

            // Décide une demande EN ATTENTE. Approuver applique l'état cible STOCKÉ sur la demande à la souscription et aux
            // réglages de l'école, dans la transaction de l'appelant ; rejeter ne touche que la demande. Renvoie l'école
            // concernée, ou NULL si la demande n'existe pas ou n'est plus en attente (déjà décidée, retirée).
            //
            // Trois gardes empêchent d'appliquer un état devenu faux depuis la demande :
            //   STALE_CHANGE_REQUEST   — le profil, la tranche ou les cycles de l'école ne sont plus ceux de la demande ;
            //   TARGET_TIER_TOO_SMALL  — l'effectif actuel dépasse déjà le plafond de la tranche demandée ;
            //   CYCLE_HAS_CLASSROOMS   — un cycle retiré contient encore des classes vivantes.
            migrationBuilder.Sql("""
                CREATE FUNCTION resolve_subscription_change_request(
                    p_id uuid,
                    p_approve boolean,
                    p_reviewer uuid,
                    p_note text)
                RETURNS uuid
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    r subscription_change_requests%ROWTYPE;
                BEGIN
                    SELECT * INTO r
                      FROM subscription_change_requests
                     WHERE "Id" = p_id AND "Status" = 'Pending' AND NOT "IsDeleted"
                       FOR UPDATE;

                    IF NOT FOUND THEN
                        RETURN NULL;
                    END IF;

                    IF NOT p_approve THEN
                        UPDATE subscription_change_requests
                           SET "Status" = 'Rejected', "ReviewedBy" = p_reviewer, "ReviewedAt" = NOW(),
                               "ReviewNote" = p_note, "UpdatedAt" = NOW()
                         WHERE "Id" = r."Id";

                        RETURN r."SchoolId";
                    END IF;

                    -- Une école SANS ligne de réglages (les plus anciennes, ou jamais enregistrées) gère tous les cycles : c'est la
                    -- valeur que l'application lui prête (SchoolSettingsDefaults.ManagedCycles), donc celle de la demande.
                    IF NOT EXISTS (
                        SELECT 1 FROM tenant_subscriptions t
                         WHERE t."SchoolId" = r."SchoolId" AND NOT t."IsDeleted"
                           AND t."ProfileType" = r."CurrentProfile" AND t."StudentQuotaTier" = r."CurrentTier")
                       OR COALESCE(
                            (SELECT s."ManagedCycles" FROM school_settings s WHERE s."SchoolId" = r."SchoolId" AND NOT s."IsDeleted"),
                            'Maternelle,Primaire,College,Lycee') <> r."CurrentCycles"
                    THEN
                        RAISE EXCEPTION 'STALE_CHANGE_REQUEST';
                    END IF;

                    IF (SELECT COUNT(*) FROM students st WHERE st."SchoolId" = r."SchoolId" AND NOT st."IsDeleted")
                           > r."TargetMaxStudentLimit" THEN
                        RAISE EXCEPTION 'TARGET_TIER_TOO_SMALL';
                    END IF;

                    IF EXISTS (
                        SELECT 1 FROM classrooms c
                         WHERE c."SchoolId" = r."SchoolId" AND NOT c."IsDeleted"
                           AND c."Cycle" = ANY (string_to_array(r."CurrentCycles", ','))
                           AND NOT (c."Cycle" = ANY (string_to_array(r."TargetCycles", ','))))
                    THEN
                        RAISE EXCEPTION 'CYCLE_HAS_CLASSROOMS';
                    END IF;

                    UPDATE tenant_subscriptions
                       SET "ProfileType" = r."TargetProfile",
                           "StudentQuotaTier" = r."TargetTier",
                           "MaxStudentLimit" = r."TargetMaxStudentLimit",
                           "SoftQuotaLimit" = r."TargetSoftQuotaLimit",
                           "IsPedagogyEnabled" = r."TargetIsPedagogyEnabled",
                           "IsFinanceEnabled" = r."TargetIsFinanceEnabled",
                           "IsInternatEnabled" = r."TargetIsInternatEnabled",
                           "IsCoranModuleEnabled" = r."TargetIsCoranModuleEnabled",
                           "HasBoarding" = r."TargetHasBoarding",
                           "OverQuotaSince" = NULL,
                           "UpdatedAt" = NOW(),
                           "UpdatedBy" = p_reviewer::text
                     WHERE "SchoolId" = r."SchoolId" AND NOT "IsDeleted";

                    -- Réglages : mis à jour, ou CRÉÉS s'ils n'existent pas (mêmes valeurs de départ que provision_school_director).
                    INSERT INTO school_settings ("Id", "SchoolId", "GradingScale", "StudentMatriculeFormat", "TeacherMatriculeFormat",
                                                 "AutoLogoutMinutes", "DateFormat", "CreatedAt", "IsDeleted",
                                                 "ProfileEtablissement", "IsPedagogyEnabled", "IsFinanceEnabled", "IsInternatEnabled",
                                                 "IsCoranModuleEnabled", "ManagedCycles")
                    VALUES (gen_random_uuid(), r."SchoolId", 20, 'ELEV-{YEAR}-{SEQ:4}', 'ENS-{YEAR}-{SEQ:3}', 10, 'dd/MM/yyyy', NOW(), FALSE,
                            r."TargetEstablishmentProfile", r."TargetIsPedagogyEnabled", r."TargetIsFinanceEnabled",
                            r."TargetIsInternatEnabled", r."TargetIsCoranModuleEnabled", r."TargetCycles")
                    ON CONFLICT ("SchoolId") DO UPDATE
                       SET "ProfileEtablissement" = EXCLUDED."ProfileEtablissement",
                           "IsPedagogyEnabled" = EXCLUDED."IsPedagogyEnabled",
                           "IsFinanceEnabled" = EXCLUDED."IsFinanceEnabled",
                           "IsInternatEnabled" = EXCLUDED."IsInternatEnabled",
                           "IsCoranModuleEnabled" = EXCLUDED."IsCoranModuleEnabled",
                           "ManagedCycles" = EXCLUDED."ManagedCycles",
                           "UpdatedAt" = NOW();

                    UPDATE subscription_change_requests
                       SET "Status" = 'Approved', "ReviewedBy" = p_reviewer, "ReviewedAt" = NOW(),
                           "ReviewNote" = p_note, "UpdatedAt" = NOW()
                     WHERE "Id" = r."Id";

                    RETURN r."SchoolId";
                END
                $$;
                """);

            // Écoles dont l'effectif dépasse le plafond NOMINAL de leur tranche — l'état réel, relu d'après les élèves
            // vivants (OverQuotaSince n'en donne que la date de premier constat). Les plafonds illimités ne ressortent jamais.
            migrationBuilder.Sql("""
                CREATE FUNCTION list_over_quota_schools()
                RETURNS TABLE (
                    school_id uuid,
                    school_name text,
                    tier text,
                    max_students integer,
                    soft_limit integer,
                    student_count bigint,
                    over_quota_since timestamptz)
                LANGUAGE sql
                STABLE
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    SELECT t."SchoolId", sc."Name"::text, t."StudentQuotaTier"::text, t."MaxStudentLimit", t."SoftQuotaLimit",
                           COUNT(st."Id"), t."OverQuotaSince"
                      FROM tenant_subscriptions t
                      JOIN schools sc ON sc."Id" = t."SchoolId" AND NOT sc."IsDeleted"
                      LEFT JOIN students st ON st."SchoolId" = t."SchoolId" AND NOT st."IsDeleted"
                     WHERE NOT t."IsDeleted" AND t."Status" = 'Active'
                     GROUP BY t."SchoolId", sc."Name", t."StudentQuotaTier", t."MaxStudentLimit", t."SoftQuotaLimit", t."OverQuotaSince"
                    HAVING COUNT(st."Id") > t."MaxStudentLimit"
                     ORDER BY COUNT(st."Id") - t."MaxStudentLimit" DESC;
                $$;
                """);

            migrationBuilder.Sql($$"""
                DO $inner$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{{AppRole}}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : retiré à PUBLIC, accordé nommément
                        -- au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text, boolean) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text, boolean) TO {{AppRole}}';
                        EXECUTE 'REVOKE ALL ON FUNCTION list_subscription_change_requests(text, uuid) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION list_subscription_change_requests(text, uuid) TO {{AppRole}}';
                        EXECUTE 'REVOKE ALL ON FUNCTION resolve_subscription_change_request(uuid, boolean, uuid, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION resolve_subscription_change_request(uuid, boolean, uuid, text) TO {{AppRole}}';
                        EXECUTE 'REVOKE ALL ON FUNCTION list_over_quota_schools() FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION list_over_quota_schools() TO {{AppRole}}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : les demandes de changement d''abonnement ne seront pas utilisables.', '{{AppRole}}';
                    END IF;
                END
                $inner$;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS list_over_quota_schools();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS resolve_subscription_change_request(uuid, boolean, uuid, text);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS list_subscription_change_requests(text, uuid);");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS provision_configured_tenant_subscription(uuid, text, text, text, integer, integer, boolean, boolean, boolean, boolean, text, boolean);");

            migrationBuilder.DropTable(
                name: "subscription_change_requests");

            migrationBuilder.DropColumn(
                name: "HasBoarding",
                table: "tenant_subscriptions");

            migrationBuilder.DropColumn(
                name: "OverQuotaSince",
                table: "tenant_subscriptions");
        }
    }
}
