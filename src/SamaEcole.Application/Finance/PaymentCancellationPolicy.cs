namespace SamaEcole.Application.Finance;

/// <summary>
/// Délai de grâce pendant lequel un versement saisi par erreur peut encore être annulé (spec
/// 2026-10-17-deletion-dependency-guard-design.md §2.4). Une erreur de caisse se remarque souvent le
/// lendemain : l'annulation reste donc possible 48 heures après l'encaissement, même si la session de
/// caisse a été clôturée entre-temps. Passé ce délai, l'annulation ordinaire est BLOQUÉE (409) ; la correction
/// passe alors par une CONTRE-PASSATION : la même opération, demandée explicitement (`Reversal`) et réservée au
/// Directeur. Le registre daté (<see cref="PaymentLedger"/>) en fait une écriture inverse datée du jour : les périodes
/// déjà arrêtées ne sont jamais réécrites, le versement d'origine y reste compté et signalé « annulé le … ».
/// Aucune colonne dédiée : une annulation faite après <see cref="DeadlineFor"/> EST une contre-passation.
///
/// Source unique : le handler d'annulation fait foi, et les écrans reçoivent <c>CancellableUntil</c>
/// calculé ici — jamais une durée recopiée côté JavaScript.
/// </summary>
public static class PaymentCancellationPolicy
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(48);

    /// <summary>Dernier instant où le versement encaissé à <paramref name="paidAt"/> peut être annulé.</summary>
    public static DateTimeOffset DeadlineFor(DateTimeOffset paidAt) => paidAt + GracePeriod;

    /// <summary>Vrai quand le versement encaissé à <paramref name="paidAt"/> ne peut plus être annulé qu'en contre-passation.</summary>
    public static bool IsPastGracePeriod(DateTimeOffset paidAt, DateTimeOffset now) => now > DeadlineFor(paidAt);
}
