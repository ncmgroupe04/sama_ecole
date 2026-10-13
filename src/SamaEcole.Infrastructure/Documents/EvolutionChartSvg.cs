using System.Globalization;
using System.Text;
using SamaEcole.Application.Internat;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Courbe d'évolution du bulletin coranique, en SVG SANS TEXTE (les dates et l'échelle sont imprimées par la mise en page, dans la
/// police du document : aucun souci de police embarquée dans le SVG). Même règles que l'écran : axe vertical arrondi à une
/// graduation lisible (au moins 10 %), niveau d'ouverture de la période en premier point, un point par jour de saisie.
/// </summary>
public static class EvolutionChartSvg
{
    public const float Width = 510;
    public const float Height = 90;

    private const float Left = 4;
    private const float Right = 6;
    private const float Top = 6;
    private const float Bottom = 6;

    private static readonly decimal[] Ladder = [10, 20, 25, 50, 75, 100];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public sealed record Result(string Svg, decimal YMax);

    /// <summary>Graduation maximale de l'axe : la plus petite de l'échelle qui contient la valeur (au-delà de 100 : la dizaine).</summary>
    public static decimal NiceMax(decimal value)
    {
        foreach (var step in Ladder)
        {
            if (step >= value)
            {
                return step;
            }
        }

        return Math.Ceiling(value / 10) * 10;
    }

    public static Result Build(DateOnly from, DateOnly to, int? openingTotalQuarters, IReadOnlyList<HizbHistoryPointDto> points)
    {
        var series = new List<(DateOnly Date, decimal Percent)>();
        if (openingTotalQuarters is { } opening)
        {
            series.Add((from, HizbRules.ProgressPercent(opening)));
        }

        series.AddRange(points.Select(p => (p.Date, p.ProgressPercent)));

        var yMax = NiceMax(series.Count == 0 ? 0 : series.Max(s => s.Percent));
        var x0 = Left;
        var x1 = Width - Right;
        var y0 = Top;
        var y1 = Height - Bottom;
        var spanDays = Math.Max(1, to.DayNumber - from.DayNumber);

        float X(DateOnly date) => x0 + (Math.Clamp(date.DayNumber - from.DayNumber, 0, spanDays) / (float)spanDays) * (x1 - x0);
        float Y(decimal percent) => y1 - (float)(Math.Clamp(percent, 0, yMax) / yMax) * (y1 - y0);
        static string F(float v) => Math.Round(v, 1).ToString("0.#", Invariant);

        var svg = new StringBuilder();
        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{F(Width)}\" height=\"{F(Height)}\" viewBox=\"0 0 {F(Width)} {F(Height)}\">");

        // Trois lignes d'échelle : 0, la moitié et le maximum de l'axe.
        foreach (var ratio in new[] { 0f, 0.5f, 1f })
        {
            var y = y1 - ratio * (y1 - y0);
            svg.Append(Invariant, $"<line x1=\"{F(x0)}\" x2=\"{F(x1)}\" y1=\"{F(y)}\" y2=\"{F(y)}\" stroke=\"#D1D5DB\" stroke-width=\"0.6\"/>");
        }

        var coordinates = series.Select(s => (X: X(s.Date), Y: Y(s.Percent))).ToList();
        if (coordinates.Count >= 2)
        {
            var line = string.Join(" L", coordinates.Select(c => $"{F(c.X)},{F(c.Y)}"));
            svg.Append(Invariant, $"<path d=\"M{F(coordinates[0].X)},{F(y1)} L{line} L{F(coordinates[^1].X)},{F(y1)} Z\" fill=\"#DCFCE7\" stroke=\"none\"/>");
            svg.Append(Invariant, $"<path d=\"M{line}\" fill=\"none\" stroke=\"#15803D\" stroke-width=\"1.6\" stroke-linejoin=\"round\" stroke-linecap=\"round\"/>");
        }

        foreach (var c in coordinates)
        {
            svg.Append(Invariant, $"<circle cx=\"{F(c.X)}\" cy=\"{F(c.Y)}\" r=\"2.4\" fill=\"#15803D\"/>");
        }

        svg.Append("</svg>");
        return new Result(svg.ToString(), yMax);
    }
}
