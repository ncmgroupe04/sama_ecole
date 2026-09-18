using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeCategory;

/// <summary>
/// POST /api/v1/finance/fee-categories — ticket JGK-F01.
/// Le SchoolId vient du JWT, jamais du client (AGENTS.md règle #10). La liste est libre : aucune
/// catégorie n'est codée en dur, l'établissement facture ce qu'il veut (Volume 1 §7.1).
/// </summary>
public record CreateFeeCategoryCommand : IRequest<CreateFeeCategoryResult>
{
    public required string Name { get; init; }

    /// <summary>Vrai pour une mensualité (due chaque mois), faux pour un frais ponctuel.</summary>
    public bool IsRecurring { get; init; }

    /// <summary>Vrai pour une catégorie de pension (module Internat) : exclue du calcul des frais
    /// scolaires ordinaires à l'inscription, incluse seulement pour un élève Interne/Demi-pensionnaire.</summary>
    public bool IsBoardingFee { get; init; }
}

public record CreateFeeCategoryResult(Guid Id, string Name, bool IsRecurring, bool IsBoardingFee);
