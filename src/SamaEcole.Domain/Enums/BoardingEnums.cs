namespace SamaEcole.Domain.Enums;

/// <summary>Genre d'un pavillon. <c>Mixte</c> n'existe que pour la reprise de données (spec Q2) : l'API le refuse.</summary>
public enum DormitoryGender
{
    Garcons,
    Filles,
    Mixte
}

/// <summary>Régime d'un séjour à l'internat. Un externe n'a simplement pas de séjour actif.</summary>
public enum BoardingRegime
{
    Interne,
    DemiPensionnaire
}

/// <summary>
/// Statut d'un lit. Seuls <c>Available</c> et <c>Maintenance</c> sont STOCKÉS (contrainte CHECK en base) ;
/// <c>Occupied</c> est projeté à la lecture depuis le séjour actif (spec N2) — jamais une seconde source de vérité.
/// </summary>
public enum BedStatus
{
    Available,
    Occupied,
    Maintenance
}

public enum BoardingLeaveReason
{
    Weekend,
    Sante,
    Famille,
    Autre
}
