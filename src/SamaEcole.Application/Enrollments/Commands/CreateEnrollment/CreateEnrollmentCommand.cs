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
/// initialise le compte financier (lignes de frais figées). L'inscription FIGE LA DETTE, elle
/// n'encaisse rien : le secrétariat n'enregistre aucun versement (AGENTS.md règle #4,
/// docs/design-references/README.md §1). Tout règlement passe ensuite par la Caisse
/// (RecordPaymentCommand). Ce qui ne figure PAS ici, à dessein :
///
///   * Le SchoolId — lu du JWT, jamais du client (AGENTS.md règle #10).
///   * L'année scolaire — l'inscription porte l'année ACTIVE, résolue serveur (mission JGK-E01).
///   * Le TotalDue — calculé serveur ; l'accepter du client laisserait fixer un montant arbitraire
///     (règle #4 : le montant d'une inscription n'est pas une donnée d'entrée).
///   * Tout encaissement — aucun versement, aucun mode de règlement : ce n'est pas le rôle de cet acte.
/// </summary>
public record CreateEnrollmentCommand : IRequest<EnrollmentReceiptDto>
{
    public required EnrollmentType Type { get; init; }

    public required Guid ClassroomId { get; init; }

    /// <summary>L'élève redouble cette classe (feature F) — coché sur le bulletin. Faux par défaut.</summary>
    public bool IsRepeating { get; init; }

    /// <summary>
    /// Élève transféré d'un autre établissement (Évolution N°7, statut IEF « Transféré »). Faux par défaut ;
    /// <see cref="PreviousSchoolName"/> nomme l'établissement d'origine (facultatif, ignoré sinon).
    /// </summary>
    public bool IsTransferredIn { get; init; }

    public string? PreviousSchoolName { get; init; }

    // --- Réinscription : élève existant ---
    public Guid? StudentId { get; init; }

    // --- Nouvelle inscription : état civil de l'élève à créer ---
    public string? FullName { get; init; }
    public DateOnly? BirthDate { get; init; }

    /// <summary>Lieu de naissance de l'élève à créer — obligatoire pour une NOUVELLE inscription (feature E).</summary>
    public string? BirthPlace { get; init; }

    public string? Gender { get; init; }
    public string? GuardianName { get; init; }
    public string? GuardianPhone { get; init; }

    // --- Régime & Hébergement (module Internat) ---

    /// <summary>Défaut Externe : un élève qui ne loge pas dans l'établissement.</summary>
    public BoardingStatus BoardingStatus { get; init; } = BoardingStatus.Externe;

    /// <summary>Chambre affectée — requis si <see cref="BoardingStatus"/> ≠ Externe (voir le Validator).</summary>
    public Guid? RoomId { get; init; }

    /// <summary>
    /// Si vrai ET qu'une catégorie FeeCategory.IsBoardingFee a un ClassFee sur la classe choisie,
    /// ajoute la ligne de pension au compte financier de l'inscription (voir BoardingFeeLineBuilder).
    /// Sans effet pour un élève Externe.
    /// </summary>
    public bool IncludeBoardingFee { get; init; }

    // --- Matières optionnelles (Évolution N°6) ---

    /// <summary>
    /// Options retenues pour l'année (identifiants de matières de classe, au plus une par groupe : sa LV2, son
    /// option scientifique…). Un groupe sans choix reçoit l'option par défaut — la plus fréquente de
    /// l'établissement. Absent du corps d'un client existant → toutes les options par défaut ; sans effet pour une
    /// classe sans groupe d'options.
    /// </summary>
    public IReadOnlyList<Guid>? SubjectOptionIds { get; init; }
}
