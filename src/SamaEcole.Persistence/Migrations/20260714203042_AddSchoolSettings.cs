using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-B02 — paramètres d'établissement.
    ///
    /// Table tenant : RLS + Global Query Filter, comme toute donnée d'école (règle #2). Contrairement
    /// au journal d'audit de JGK-A05, elle est bien MODIFIABLE : le rôle applicatif y garde UPDATE.
    ///
    /// La fonction provision_school_director est ÉTENDUE (CREATE OR REPLACE — on ne modifie jamais une
    /// migration déjà appliquée) pour poser les valeurs par défaut au moment même de la création de
    /// l'école, comme l'exige le critère du ticket. Le Super Admin n'a aucun tenant : sans cela, il
    /// ne pourrait pas insérer cette ligne, exactement comme il ne peut pas insérer dans `users`.
    ///
    /// La garde reste la même — la fonction refuse toujours d'agir sur une école déjà provisionnée,
    /// donc aucune escalade vers le tenant d'autrui. Elle passe de LANGUAGE sql à plpgsql : il lui
    /// faut désormais enchaîner deux INSERT et retourner l'identifiant du seul premier.
    /// </summary>
    public partial class AddSchoolSettings : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "school_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SchoolId = table.Column<Guid>(type: "uuid", nullable: false),
                    GradingScale = table.Column<int>(type: "integer", nullable: false),
                    StudentMatriculeFormat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TeacherMatriculeFormat = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    AutoLogoutMinutes = table.Column<int>(type: "integer", nullable: false),
                    DateFormat = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
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
                    table.PrimaryKey("PK_school_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_school_settings_schools_SchoolId",
                        column: x => x.SchoolId,
                        principalTable: "schools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_school_settings_SchoolId",
                table: "school_settings",
                column: "SchoolId",
                unique: true);

            migrationBuilder.Sql("ALTER TABLE school_settings ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("""
                CREATE POLICY school_settings_tenant_isolation ON school_settings
                    USING ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid)
                    WITH CHECK ("SchoolId" = NULLIF(current_setting('app.current_school_id', true), '')::uuid);
                """);

            // Les valeurs par défaut sont écrites ICI, dans le provisionnement, et doivent rester
            // synchronisées avec SchoolSettingsDefaults (Domain). Elles sont dupliquées à dessein :
            // une école neuve doit avoir ses réglages AVANT que la moindre ligne de C# ne tourne.
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
                        EXECUTE 'GRANT SELECT, INSERT, UPDATE ON school_settings TO {AppRole}';
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS school_settings_tenant_isolation ON school_settings;");

            migrationBuilder.DropTable(
                name: "school_settings");

            // Retour à la version de AddSchoolProvisioning : Directeur seul, sans réglages.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION provision_school_director(
                    p_school_id uuid,
                    p_email text,
                    p_password_hash text,
                    p_full_name text,
                    p_role text)
                RETURNS uuid
                LANGUAGE sql
                SECURITY DEFINER
                SET search_path = public
                AS $$
                    INSERT INTO users ("Id", "SchoolId", "Email", "PasswordHash", "FullName", "Role",
                                       "Status", "AccessFailedCount", "CreatedAt", "IsDeleted")
                    SELECT gen_random_uuid(), p_school_id, p_email, p_password_hash, p_full_name,
                           p_role, 'Active', 0, NOW(), FALSE
                    WHERE NOT EXISTS (
                        SELECT 1 FROM users u WHERE u."SchoolId" = p_school_id
                    )
                    RETURNING "Id";
                $$;
                """);
        }
    }
}
