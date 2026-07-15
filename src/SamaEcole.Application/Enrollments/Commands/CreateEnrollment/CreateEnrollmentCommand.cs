using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Enrollments.Commands.CreateEnrollment;

/// <summary>
/// POST /api/v1/enrollments — ticket JGK-E01. Un seul acte transactionnel qui, selon le
/// <see cref="Type"/> :
///
///   * NewEnrollment — CRÉE l'élève (et génère son matricule, JGK-D01/B02) puis l'inscrit ;
///   * ReEnrollment — inscrit un élève DÉJÀ connu (<see cref="StudentId"/> requis), sans nouveau matricule.
///
/// Dans les deux cas, le service calcule le montant dû à partir du barème de la classe (JGK-F01) et
/// initialise le compte financier (lignes de frais figées). Ce qui ne figure PAS ici, à dessein :
///
///   * Le SchoolId — lu du JWT, jamais du client (AGENTS.md règle #10).
///   * L'année scolaire — l'inscription porte l'année ACTIVE, résolue serveur (mission JGK-E01).
///   * Le TotalDue — calculé serveur ; l'accepter du client laisserait fixer un montant arbitraire
///     (règle #4 : le montant d'une inscription n'est pas une donnée d'entrée).
/// </summary>
public record CreateEnrollmentCommand : IRequest<EnrollmentReceiptDto>
{
    public required EnrollmentType Type { get; init; }

    public required Guid ClassroomId { get; init; }

    // --- Réinscription : élève existant ---
    public Guid? StudentId { get; init; }

    // --- Nouvelle inscription : état civil de l'élève à créer ---
    public string? FullName { get; init; }
    public DateOnly? BirthDate { get; init; }
    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }
}
