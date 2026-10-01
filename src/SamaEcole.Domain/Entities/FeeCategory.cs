using SamaEcole.Domain.Common;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Catégorie de frais paramétrable par l'établissement (ticket JGK-F01, Volume 1 §7.1) :
/// inscription, réinscription, mensualité, examens, uniforme, transport, cantine, autres.
///
/// La liste est LIBRE, comme la nomenclature des classes : chaque école facture ce qu'elle veut. On
/// ne code donc aucune énumération de catégories — l'établissement les crée lui-même.
///
/// <see cref="IsRecurring"/> distingue une mensualité (due chaque mois) d'un frais ponctuel
/// (inscription, uniforme). Ce n'est pas décoratif : le calcul du montant dû à l'inscription
/// (ticket JGK-E01) multipliera une mensualité par le nombre de mois, jamais un frais ponctuel.
/// </summary>
public class FeeCategory : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public required string Name { get; set; }

    /// <summary>Vrai pour une mensualité (due chaque mois), faux pour un frais ponctuel.</summary>
    public bool IsRecurring { get; set; }

    /// <summary>
    /// Désigne cette catégorie comme frais d'internat (module Internat) : elle peut être incluse
    /// automatiquement sur l'inscription d'un élève Interne/Demi-pensionnaire (voir
    /// BoardingFeeLineBuilder), en plus des ClassFee ordinaires appliqués à tous les élèves de la
    /// classe. Faux par défaut — un choix explicite de l'école, comme IsRecurring.
    /// </summary>
    public bool IsBoardingFee { get; set; }

    /// <summary>
    /// Frais FACULTATIF (uniforme, tenue de sport, cantine…) : la famille le choisit à l'inscription et à la
    /// réinscription au lieu de le devoir d'office. Faux par défaut — un frais est OBLIGATOIRE tant que le
    /// Directeur ne l'a pas explicitement déclaré optionnel. C'est ce défaut qui protège inscription et
    /// mensualité : la liste des catégories étant libre, aucun nom n'est codé en dur pour les reconnaître.
    /// Une pension (<see cref="IsBoardingFee"/>) ne peut pas être optionnelle : elle a son propre choix
    /// (régime d'hébergement + IncludeBoardingFee).
    /// </summary>
    public bool IsOptional { get; set; }
}
