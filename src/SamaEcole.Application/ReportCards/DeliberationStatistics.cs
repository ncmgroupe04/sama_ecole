using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>PV d'une période (semestre/trimestre) ou PV ANNUEL de fin d'année.</summary>
public enum DeliberationScope
{
    Period,
    Annual
}

/// <param name="Enrolled">Effectif inscrit dans la classe.</param>
/// <param name="Present">Élèves ayant composé (période) ou ayant au moins une moyenne de période (annuel).</param>
/// <param name="Ranked">Élèves classés : ceux qui ont une moyenne (générale, ou annuelle).</param>
/// <param name="Passed">Classés dont la moyenne atteint la moitié du barème (10/20).</param>
public sealed record GenderBreakdown(int Enrolled, int Present, int Ranked, int Passed)
{
    /// <summary>Taux de réussite en % des CLASSÉS ; null si personne n'est classé (jamais un 0 % trompeur).</summary>
    public decimal? PassRate => Ranked == 0 ? null : Math.Round(Passed * 100m / Ranked, 2);
}

/// <summary>
/// Tableau récapitulatif du procès-verbal du conseil de classe (Évolution N°7), ventilé Filles / Garçons comme
/// l'attendent les Inspections d'Académie : effectif, présents, classés, admis (moyenne ≥ moitié du barème) et
/// taux de réussite ; moyenne de la classe, extrêmes ; décompte des distinctions (période) ou des décisions
/// (annuel — la décision saisie, à défaut la proposition). Pur : calculé à partir des bulletins, jamais un
/// second calcul de moyenne.
/// </summary>
public sealed record DeliberationStatistics(
    GenderBreakdown Girls,
    GenderBreakdown Boys,
    GenderBreakdown Total,
    decimal? ClassAverage,
    decimal? Highest,
    decimal? Lowest,
    int Felicitations,
    int HonorRoll,
    int Encouragements,
    int Admitted,
    int AllowedToRepeat,
    int Excluded)
{
    /// <summary>La moyenne retenue pour le PV : générale de la période, ou annuelle ; null si l'élève n'est pas classé.</summary>
    public static decimal? AverageOf(ReportCardDto card, DeliberationScope scope) => scope == DeliberationScope.Annual
        ? card.AnnualAverage
        : card.TotalCoefficients > 0 ? card.GeneralAverage : null;

    /// <summary>Décision imprimée au PV annuel : celle du conseil, à défaut la proposition.</summary>
    public static CouncilDecision? DecisionOf(ReportCardDto card) => card.CouncilDecision ?? card.ProposedCouncilDecision;

    public static bool IsGirl(ReportCardDto card) =>
        string.Equals(card.StudentGender?.Trim(), "F", StringComparison.OrdinalIgnoreCase);

    public static DeliberationStatistics Compute(IReadOnlyList<ReportCardDto> cards, DeliberationScope scope)
    {
        GenderBreakdown Breakdown(IEnumerable<ReportCardDto> group)
        {
            var list = group.ToList();
            var ranked = list.Select(c => (Card: c, Average: AverageOf(c, scope))).Where(x => x.Average is not null).ToList();
            var present = scope == DeliberationScope.Annual
                ? list.Count(c => c.TermRecaps.Any(t => t.Average is not null))
                : list.Count(c => c.SatComposition || c.TotalCoefficients > 0);

            return new GenderBreakdown(
                list.Count,
                present,
                ranked.Count,
                ranked.Count(x => x.Average!.Value >= x.Card.GradingScale / 2m));
        }

        var averages = cards.Select(c => AverageOf(c, scope)).Where(a => a is not null).Select(a => a!.Value).ToList();
        var decisions = scope == DeliberationScope.Annual ? cards.Select(DecisionOf).ToList() : [];

        return new DeliberationStatistics(
            Breakdown(cards.Where(IsGirl)),
            Breakdown(cards.Where(c => !IsGirl(c))),
            Breakdown(cards),
            averages.Count == 0 ? null : Math.Round(averages.Average(), 2),
            averages.Count == 0 ? null : averages.Max(),
            averages.Count == 0 ? null : averages.Min(),
            cards.Count(c => c.DisciplinaryMention == DisciplinaryMention.Felicitations),
            cards.Count(c => c.DisciplinaryMention == DisciplinaryMention.TableauHonneur),
            cards.Count(c => c.DisciplinaryMention == DisciplinaryMention.Encouragements),
            decisions.Count(d => d == CouncilDecision.Admitted),
            decisions.Count(d => d == CouncilDecision.AllowedToRepeat),
            decisions.Count(d => d == CouncilDecision.Excluded));
    }
}
