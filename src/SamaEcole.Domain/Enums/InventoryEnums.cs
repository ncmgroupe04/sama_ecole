namespace SamaEcole.Domain.Enums;

/// <summary>
/// État d'un LOT de biens (module Inventaire). Un lot porte UN état dominant, pas une répartition :
/// une école qui veut distinguer « 120 tables-bancs en bon état » de « 50 à réparer » crée deux lots
/// et transfère les quantités de l'un à l'autre par un ajustement. Arbitrage acté à la conception du
/// module — voir docs/Volume_3_DDS.md §Inventaire.
/// </summary>
public enum ItemCondition
{
    Neuf,
    Bon,
    AReparer,
    HorsService
}

/// <summary>
/// Nature d'une ligne du journal de stock, telle qu'elle est ÉCRITE en base.
///
/// Le SENS (ajout ou retrait) découle du type, jamais du signe de la quantité :
/// <c>StockMovement.Quantity</c> est toujours strictement positif, contrainte CHECK à l'appui. Une
/// quantité signée invite tôt ou tard à un « -0 » ou à un double retrait.
///
/// Chaque membre correspond donc à UNE arithmétique sans ambiguïté sur le couple
/// (QuantityTotal, QuantityAvailable) — c'est ce qui rend une ligne isolée du journal lisible sans
/// rejouer l'historique. Deux membres seraient tentants à fusionner (Sortie et MiseAuRebut ont le
/// même calcul) : ils restent distincts parce qu'un contrôle de l'IEF ne lit pas de la même façon
/// « transféré à une autre école » et « cassé, réformé ».
///
/// Ce n'est PAS l'énumération exposée par l'API : voir <c>StockMovementRequestType</c>, qui n'admet
/// que les quatre mouvements qu'un utilisateur saisit directement. Les mouvements liés aux prêts sont
/// produits exclusivement par les endpoints d'affectation, pour qu'aucun appel direct ne puisse
/// désynchroniser une fiche de décharge de son stock.
/// </summary>
public enum StockMovementType
{
    /// <summary>Réception : dotation de l'État/mairie, achat, don. Total +q, disponible +q.</summary>
    Entree,

    /// <summary>Sortie définitive : consommable distribué, transfert vers un autre établissement. Total -q, disponible -q.</summary>
    Sortie,

    /// <summary>Recomptage à la hausse après inventaire physique. Total +q, disponible +q.</summary>
    AjustementPositif,

    /// <summary>Recomptage à la baisse après inventaire physique. Total -q, disponible -q.</summary>
    AjustementNegatif,

    /// <summary>Prêt/attribution à un bénéficiaire. Disponible -q, total INCHANGÉ : le bien reste au patrimoine.</summary>
    Attribution,

    /// <summary>Retour d'un prêt. Disponible +q, total inchangé.</summary>
    Restitution,

    /// <summary>Réforme d'un bien PRÉSENT dans l'école (cassé, hors service). Total -q, disponible -q.</summary>
    MiseAuRebut,

    /// <summary>
    /// Perte constatée sur un bien PRÊTÉ et jamais restitué. Total -q, disponible INCHANGÉ — les
    /// unités concernées étaient déjà sorties du disponible par l'attribution. Sans ce membre, une
    /// perte sur prêt s'écrirait « Restitution puis MiseAuRebut » : arithmétiquement juste, mais le
    /// journal affirmerait un retour qui n'a jamais eu lieu.
    /// </summary>
    PerteSurPret
}

/// <summary>
/// Nature du bénéficiaire d'une affectation. Détermine LAQUELLE des trois clés étrangères de
/// <c>ItemAssignment</c> est renseignée — une contrainte CHECK en base impose la cohérence, pour
/// qu'aucune fiche ne puisse désigner « un élève » sans élève.
/// </summary>
public enum AssignmentBeneficiaryType
{
    Eleve,
    Enseignant,

    /// <summary>Personnel administratif (utilisateur de la plateforme) : PC du Secrétariat, clés, etc.</summary>
    Personnel
}

/// <summary>Cycle de vie d'une fiche de prêt/attribution.</summary>
public enum AssignmentStatus
{
    EnCours,
    Restitue,
    PartiellementRestitue,

    /// <summary>Non restitué et déclaré perdu : la part manquante est sortie du patrimoine (PerteSurPret).</summary>
    Perdu
}
