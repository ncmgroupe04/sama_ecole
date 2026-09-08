using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Audit sécurité — une même adresse e-mail pouvait ouvrir DEUX établissements.
    ///
    /// <para><b>La faille.</b> <c>users.Email</c> portait un index unique GLOBAL, mais sur
    /// <c>varchar</c> : la comparaison — donc l'unicité — était sensible à la casse.
    /// <c>Directeur@ecole.sn</c> et <c>directeur@ecole.sn</c> étaient deux comptes acceptés, alors
    /// que le login (<c>auth_find_user_by_email</c>, <c>lower() = lower()</c>) les confondait. Deux des
    /// trois chemins de création de compte (inscription self-service, création directe) ne
    /// normalisaient pas non plus la casse avant l'écriture.</para>
    ///
    /// <para><b>Le correctif, côté base.</b></para>
    /// <list type="number">
    ///   <item>Refus de migrer s'il existe déjà des doublons de casse parmi les comptes VIVANTS :
    ///         un humain doit trancher lequel garde l'adresse (requête de l'audit) — on ne fusionne
    ///         ni ne supprime rien ici (AGENTS.md règle #6).</item>
    ///   <item><c>users.Email</c> passe en <c>citext</c> : unicité et comparaisons insensibles à la
    ///         casse partout, sans dépendre de la discipline de chaque appelant.</item>
    ///   <item><c>IX_users_Email</c> recréé unique + <c>WHERE "IsDeleted" = false</c> : un compte
    ///         soft-deleted ne réserve plus l'adresse (cohérent avec <c>auth_find_user_by_email</c>,
    ///         qui ignore déjà les comptes supprimés).</item>
    ///   <item><c>provision_school_director</c> gagne une garde e-mail explicite (défense en
    ///         profondeur, AGENTS.md règle #2) : deux approbations concurrentes qui franchissent le
    ///         pré-contrôle applicatif obtiennent un refus net (23505) et non un chemin d'erreur brut.</item>
    /// </list>
    /// </summary>
    public partial class EnforceCaseInsensitiveUserEmail : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Garde-fou : la contrainte stricte ne peut pas s'installer si la base contient déjà des
            //    doublons de casse. On échoue AVANT de rien changer, avec un message actionnable —
            //    plutôt que de laisser CREATE UNIQUE INDEX planter sur une valeur au hasard, ou (pire)
            //    de « réparer » des identités de compte dans une migration.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    v_dups int;
                BEGIN
                    SELECT count(*) INTO v_dups FROM (
                        SELECT lower("Email")
                        FROM   users
                        WHERE  "IsDeleted" = false
                        GROUP  BY lower("Email")
                        HAVING count(*) > 1
                    ) d;

                    IF v_dups > 0 THEN
                        RAISE EXCEPTION
                            'Migration EnforceCaseInsensitiveUserEmail : % adresse(s) e-mail en doublon (casse ignoree) parmi les comptes actifs de users. Resolvez-les manuellement (un seul compte garde l''adresse, les autres sont renommes ou desactives) avant de rejouer cette migration.', v_dups;
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                table: "users");

            // 2. Extension citext AVANT le changement de type (elle définit le type). « trusted »
            //    depuis PostgreSQL 13 : le rôle propriétaire des migrations l'installe sans être
            //    superutilisateur. Émet CREATE EXTENSION IF NOT EXISTS — idempotent.
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:citext", ",,");

            // 3. varchar -> citext. Cast d'affectation implicite : pas de clause USING nécessaire, les
            //    octets stockés ne bougent pas, seule la sémantique de comparaison devient insensible
            //    à la casse.
            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "users",
                type: "citext",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(255)",
                oldMaxLength: 255);

            // 4. Index unique GLOBAL, désormais insensible à la casse (citext) ET filtré sur les
            //    comptes vivants.
            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true,
                filter: "\"IsDeleted\" = false");

            // 5. provision_school_director : corps ACTUEL (migration AddSchoolSettings) + garde e-mail.
            //    L'index citext ci-dessus reste le rempart réel ; ce contrôle rend l'échec explicite
            //    et le sort du chemin « PostgresException brute » lorsqu'il est atteint par une course.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION provision_school_director(
                    p_school_id uuid,
                    p_email text,
                    p_password_hash text,
                    p_full_name text,
                    p_role text)
                RETURNS uuid
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_director_id uuid;
                BEGIN
                    -- Garde inchangée : amorçage d'une école VIERGE uniquement. Une école qui a déjà un
                    -- utilisateur n'est plus à provisionner — pas d'injection de Directeur chez autrui.
                    IF EXISTS (SELECT 1 FROM users u WHERE u."SchoolId" = p_school_id) THEN
                        RETURN NULL;
                    END IF;

                    -- Défense en profondeur (AGENTS.md règle #2) : un e-mail n'identifie qu'un compte
                    -- VIVANT sur toute la plateforme. Deux approbations concurrentes portant le même
                    -- e-mail peuvent franchir le pré-contrôle applicatif ; ici, la seconde est refusée
                    -- nettement (SQLSTATE 23505), traduite en 409 par SchoolProvisioningStore.
                    IF EXISTS (SELECT 1 FROM users u
                               WHERE lower(u."Email") = lower(p_email) AND u."IsDeleted" = FALSE) THEN
                        RAISE EXCEPTION 'Cet e-mail identifie deja un compte' USING ERRCODE = 'unique_violation';
                    END IF;

                    INSERT INTO users ("Id", "SchoolId", "Email", "PasswordHash", "FullName", "Role",
                                       "Status", "AccessFailedCount", "CreatedAt", "IsDeleted")
                    VALUES (gen_random_uuid(), p_school_id, p_email, p_password_hash, p_full_name,
                            p_role, 'Active', 0, NOW(), FALSE)
                    RETURNING "Id" INTO v_director_id;

                    INSERT INTO school_settings ("Id", "SchoolId", "GradingScale", "StudentMatriculeFormat",
                                                 "TeacherMatriculeFormat", "AutoLogoutMinutes", "DateFormat",
                                                 "CreatedAt", "IsDeleted")
                    VALUES (gen_random_uuid(), p_school_id, 20, 'ELEV-{YEAR}-{SEQ:4}',
                            'ENS-{YEAR}-{SEQ:3}', 10, 'dd/MM/yyyy', NOW(), FALSE)
                    ON CONFLICT ("SchoolId") DO NOTHING;

                    RETURN v_director_id;
                END
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- CREATE OR REPLACE réinitialise les droits : il faut les reposer.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_school_director(uuid, text, text, text, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_school_director(uuid, text, text, text, text) TO {AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Retour au corps SANS garde e-mail (version de AddSchoolSettings).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION provision_school_director(
                    p_school_id uuid,
                    p_email text,
                    p_password_hash text,
                    p_full_name text,
                    p_role text)
                RETURNS uuid
                LANGUAGE plpgsql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                DECLARE
                    v_director_id uuid;
                BEGIN
                    IF EXISTS (SELECT 1 FROM users u WHERE u."SchoolId" = p_school_id) THEN
                        RETURN NULL;
                    END IF;

                    INSERT INTO users ("Id", "SchoolId", "Email", "PasswordHash", "FullName", "Role",
                                       "Status", "AccessFailedCount", "CreatedAt", "IsDeleted")
                    VALUES (gen_random_uuid(), p_school_id, p_email, p_password_hash, p_full_name,
                            p_role, 'Active', 0, NOW(), FALSE)
                    RETURNING "Id" INTO v_director_id;

                    INSERT INTO school_settings ("Id", "SchoolId", "GradingScale", "StudentMatriculeFormat",
                                                 "TeacherMatriculeFormat", "AutoLogoutMinutes", "DateFormat",
                                                 "CreatedAt", "IsDeleted")
                    VALUES (gen_random_uuid(), p_school_id, 20, 'ELEV-{YEAR}-{SEQ:4}',
                            'ENS-{YEAR}-{SEQ:3}', 10, 'dd/MM/yyyy', NOW(), FALSE)
                    ON CONFLICT ("SchoolId") DO NOTHING;

                    RETURN v_director_id;
                END
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_school_director(uuid, text, text, text, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_school_director(uuid, text, text, text, text) TO {AppRole}';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropIndex(
                name: "IX_users_Email",
                table: "users");

            migrationBuilder.AlterColumn<string>(
                name: "Email",
                table: "users",
                type: "character varying(255)",
                maxLength: 255,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "citext");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:citext", ",,");

            migrationBuilder.CreateIndex(
                name: "IX_users_Email",
                table: "users",
                column: "Email",
                unique: true);
        }
    }
}
