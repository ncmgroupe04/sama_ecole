using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// Ticket JGK-B01 — création du tout premier compte d'un établissement.
    ///
    /// LE PROBLÈME : `users` est sous policy RLS depuis AddAuthentication. Un Super Admin n'a par
    /// définition AUCUN schoolId — sa session ne satisfait donc le WITH CHECK d'aucune ligne, et son
    /// INSERT est rejeté (« new row violates row-level security policy »). Sans issue, il ne peut pas
    /// créer le Directeur d'une école neuve… et l'école reste inaccessible à tout jamais.
    ///
    /// LA SOLUTION, et pourquoi elle ne rouvre pas le multi-tenant : une fonction SECURITY DEFINER
    /// qui REFUSE d'agir si l'établissement possède déjà le moindre utilisateur. Elle ne sait donc
    /// faire qu'une chose — amorcer une école vierge. Même appelée avec un SchoolId arbitraire par du
    /// code fautif ou une injection, elle ne peut PAS glisser un Directeur dans une école existante :
    /// aucune escalade possible vers le tenant d'autrui. Elle ne lit rien, ne modifie rien, ne
    /// supprime rien.
    ///
    /// `search_path` figé sur public : sinon un rôle capable de créer un schéma pourrait y placer une
    /// fausse table `users` et détourner l'exécution, qui tourne avec les droits du PROPRIÉTAIRE.
    /// </summary>
    public partial class AddSchoolProvisioning : Migration
    {
        private const string AppRole = "sama_ecole_app";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE FUNCTION provision_school_director(
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
                    -- LA garde : amorçage d'une école VIERGE uniquement. Un établissement qui a déjà
                    -- un utilisateur n'est plus à provisionner, et cette porte doit lui rester fermée.
                    WHERE NOT EXISTS (
                        SELECT 1 FROM users u WHERE u."SchoolId" = p_school_id
                    )
                    RETURNING "Id";
                $$;
                """);

            migrationBuilder.Sql($"""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = '{AppRole}') THEN
                        -- Contourner la RLS ne doit jamais être un droit par défaut : on le retire à
                        -- PUBLIC avant de l'accorder nommément au seul rôle applicatif.
                        EXECUTE 'REVOKE ALL ON FUNCTION provision_school_director(uuid, text, text, text, text) FROM PUBLIC';
                        EXECUTE 'GRANT EXECUTE ON FUNCTION provision_school_director(uuid, text, text, text, text) TO {AppRole}';
                    ELSE
                        RAISE WARNING 'Rôle % absent : la création d''établissement ne fonctionnera pas.', '{AppRole}';
                    END IF;
                END
                $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS provision_school_director(uuid, text, text, text, text);");
        }
    }
}
