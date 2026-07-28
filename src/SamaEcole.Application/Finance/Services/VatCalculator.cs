namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Calculateur PUR (aucun accès base, aucun effet de bord) de la TVA comprise dans un montant TTC — la
/// SEULE formule officielle du domaine, partagée par l'encaissement (<c>RecordPaymentCommandHandler</c>,
/// TVA collectée) et la dépense (<c>CreateDisbursementCommandHandler</c>, TVA déductible), pour qu'une
/// évolution de la formule ne se fasse jamais qu'à UN seul endroit.
/// </summary>
public static class VatCalculator
{
    /// <summary>
    /// TVA « en dedans » : <paramref name="amountIncludingVat"/> est un montant TTC (de l'argent qui a
    /// réellement changé de main — un encaissement ou une dépense ne peuvent être qu'un total TTC), le
    /// taux en est extrait plutôt qu'ajouté par-dessus. <paramref name="vatRate"/> null (non assujetti,
    /// ex. scolarité exonérée ou salaires) renvoie 0, jamais une valeur inventée.
    /// </summary>
    public static decimal ComputeVatAmount(decimal amountIncludingVat, decimal? vatRate)
    {
        if (vatRate is null || vatRate <= 0)
        {
            return 0m;
        }

        return Math.Round(amountIncludingVat * vatRate.Value / (1 + vatRate.Value), 2, MidpointRounding.AwayFromZero);
    }
}
