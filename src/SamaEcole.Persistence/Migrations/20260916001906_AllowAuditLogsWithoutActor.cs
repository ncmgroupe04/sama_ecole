using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SamaEcole.Persistence.Migrations
{
    /// <summary>
    /// <c>append_audit_log</c> tolère désormais un acteur QUI N'EXISTE PLUS : le <c>UserId</c> est
    /// écrit à NULL au lieu de faire échouer l'insertion sur la clé étrangère vers <c>users</c>.
    ///
    /// Le cas est né avec la purge élargie du 15/09/2026 (ExtendResetSchoolDataToConfiguration), qui
    /// supprime les comptes du personnel : un membre du personnel déjà connecté garde un jeton d'accès
    /// valide jusqu'à 15 minutes (Auth.AccessTokenMinutes). Sa première action journalisée après la
    /// purge insérait alors une ligne d'audit pointant vers un compte disparu — violation 23503, donc
    /// une erreur 500 qui faisait perdre À LA FOIS l'action et sa trace.
    ///
    /// Écrire NULL est le moindre mal, et c'est cohérent avec ce que la purge fait déjà des entrées
    /// existantes : elle les DÉTACHE plutôt que de les effacer (l'écran affiche « Compte supprimé »).
    /// Une action reste ainsi toujours journalisée — perdre la trace serait plus grave que perdre le
    /// nom de son auteur, d'autant que l'audit garde le module, l'action, l'horodatage et l'adresse IP.
    ///
    /// <c>CREATE OR REPLACE</c> : même signature et même type de retour, le propriétaire et les GRANT
    /// posés par AddAuditLogAppendFunction sont conservés.
    /// </summary>
    public partial class AllowAuditLogsWithoutActor : Migration
    {
        private const string Body = """
            INSERT INTO audit_logs (
                "Id", "SchoolId", "UserId", "Module", "Action", "Success", "FailureReason",
                "IpAddress", "OccurredAt", "CreatedAt", "IsDeleted")
            VALUES (
                gen_random_uuid(), p_school_id, {0}, p_module, p_action, p_success,
                p_failure_reason, p_ip_address, p_occurred_at, NOW(), FALSE);
            """;

        /// <summary>
        /// L'acteur n'est retenu que s'il existe encore. Sous-requête plutôt que gestion d'exception :
        /// la fonction est en LANGUAGE sql, et un bloc d'exception y imposerait de passer en plpgsql
        /// pour un cas qui se décrit en une ligne.
        /// </summary>
        private const string ActorIfStillExists =
            """(SELECT u."Id" FROM users u WHERE u."Id" = p_user_id)""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(Function(string.Format(Body, ActorIfStillExists)));

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            => migrationBuilder.Sql(Function(string.Format(Body, "p_user_id")));

        private static string Function(string body) => $"""
            CREATE OR REPLACE FUNCTION append_audit_log(
                p_school_id uuid,
                p_user_id uuid,
                p_module text,
                p_action text,
                p_success boolean,
                p_failure_reason text,
                p_ip_address text,
                p_occurred_at timestamptz)
            RETURNS void
            LANGUAGE sql
            SECURITY DEFINER
            SET search_path = public
            AS $$
            {body}
            $$;
            """;
    }
}
