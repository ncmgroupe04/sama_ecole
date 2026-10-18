namespace SamaEcole.Application.Finance;

/// <summary>
/// Délai de grâce pendant lequel un versement saisi par erreur peut encore être annulé (spec
/// 2026-10-17-deletion-dependency-guard-design.md §2.4). Une erreur de caisse se remarque souvent le
/// lendemain : l'annulation reste donc possible 48 heures après l'encaissement, même si la session de
/// caisse a été clôturée entre-temps. Passé ce délai, l'annulation est BLOQUÉE définitivement ; la
/// correction passera alors par une contre-passation (écriture inverse datée du jour), non encore livrée.
///
/// Source unique : le handler d'annulation fait foi, et les écrans reçoivent <c>CancellableUntil</c>
/// calculé ici — jamais une durée recopiée côté JavaScript.
/// </summary>
public static class PaymentCancellationPolicy
{
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(48);

    /// <summary>Dernier instant où le versement encaissé à <paramref name="paidAt"/> peut être annulé.</summary>
    public static DateTimeOffset DeadlineFor(DateTimeOffset paidAt) => paidAt + GracePeriod;
}
