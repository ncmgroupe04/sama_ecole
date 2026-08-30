using MediatR;

namespace SamaEcole.Application.StateIntegration.Queries.VerifyMutationCertificate;

/// <summary>
/// GET /api/v1/state-integration/certificates/verify/{token} — vérification PUBLIQUE et ANONYME d'un
/// certificat de mutation, depuis le QR code imprimé dessus (Volume 1 §23.5, ticket JGK-M06).
///
/// Pas de <c>IAuditableRequest</c> : l'appelant n'est pas authentifié, il n'y a personne à
/// journaliser. Pas de tenant non plus — la lecture passe par la fonction SECURITY DEFINER
/// <c>verify_mutation_certificate</c>, seule habilitée à voir la ligne hors RLS.
///
/// La réponse est délibérément PAUVRE : statut, numéro, date, établissement émetteur. Aucune donnée
/// de l'élève — le QR est lisible par quiconque photographie le papier.
/// </summary>
public record VerifyMutationCertificateQuery(string Token)
    : IRequest<MutationCertificateVerificationResult>;

/// <summary>
/// <paramref name="Status"/> vaut <c>valid</c>, <c>revoked</c> ou <c>unknown</c>. Les autres champs
/// ne sont renseignés que pour <c>valid</c> / <c>revoked</c> — un code inconnu ne renvoie rien
/// d'autre que son statut, pour ne pas confirmer par recoupement l'existence d'un numéro voisin.
/// </summary>
public record MutationCertificateVerificationResult(
    string Status,
    string? CertificateNumber,
    DateOnly? IssuedOn,
    string? IssuingSchoolName,
    DateTimeOffset? RevokedAt)
{
    public static readonly MutationCertificateVerificationResult Unknown =
        new("unknown", null, null, null, null);
}
