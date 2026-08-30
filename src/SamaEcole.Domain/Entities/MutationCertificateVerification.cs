namespace SamaEcole.Domain.Entities;

/// <summary>
/// Ligne renvoyée par la fonction PostgreSQL SECURITY DEFINER
/// <c>verify_mutation_certificate(code)</c> — la vérification PUBLIQUE d'un certificat de mutation
/// scanné depuis son QR code (Volume 1 §23.5).
///
/// <see cref="StudentMutationCertificate"/> reste une table tenant normale (RLS + Global Query Filter,
/// AGENTS.md règle #2). C'est la fonction, pas cette table, qui est atteignable sans tenant — de façon
/// auditée, au périmètre minimal : elle ne révèle RIEN de l'élève (ni nom, ni date de naissance, ni
/// IEN, ni classe), seulement de quoi confirmer l'authenticité du PAPIER présenté.
///
/// Entité SANS CLÉ ni table/vue propre : interrogée uniquement via FromSqlRaw.
/// </summary>
public class MutationCertificateVerification
{
    /// <summary><c>valid</c>, <c>revoked</c>, ou — aucune ligne renvoyée — traité comme <c>unknown</c>.</summary>
    public string Status { get; set; } = string.Empty;

    public string CertificateNumber { get; set; } = string.Empty;

    public DateOnly IssuedOn { get; set; }

    /// <summary>Établissement émetteur — pour rapprocher le certificat présenté, jamais l'élève.</summary>
    public string IssuingSchoolName { get; set; } = string.Empty;

    public DateTimeOffset? RevokedAt { get; set; }
}
