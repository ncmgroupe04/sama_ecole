namespace SamaEcole.Application.RollBook;

/// <summary>
/// Compteurs de demi-journées d'un élève, d'une classe ou d'un niveau, et taux qui en découlent
/// (spec §2.4). Pur, additif : la classe et le niveau SOMMENT les compteurs puis divisent — jamais de
/// moyenne de taux, qui donnerait autant de poids à une classe de 5 élèves qu'à une de 50.
///
/// Dénominateur = demi-journées RENSEIGNÉES (<see cref="Recorded"/>) : une case sans saisie ni appel ne
/// compte ni comme présence ni comme absence. La <see cref="Coverage"/> dit quelle part des
/// demi-journées attendues est renseignée, pour qu'un cahier presque vide ne passe pas pour un 100 %.
/// Une absence PARTIELLE compte comme présence (comme le rapport d'assiduité existant) et garde son compteur.
/// </summary>
public sealed record AttendanceCounts(
    int Present = 0,
    int Late = 0,
    int PartialAbsence = 0,
    int JustifiedAbsence = 0,
    int UnjustifiedAbsence = 0,
    int LateMinutes = 0)
{
    public static readonly AttendanceCounts Zero = new();

    /// <summary>Demi-journées renseignées.</summary>
    public int Recorded => Present + Late + PartialAbsence + JustifiedAbsence + UnjustifiedAbsence;

    public int Absences => JustifiedAbsence + UnjustifiedAbsence;

    /// <summary>(Présents + retards + absences partielles) ÷ renseignées.</summary>
    public decimal? PresenceRate => Rate(Present + Late + PartialAbsence);

    /// <summary>(Justifiées + non justifiées) ÷ renseignées.</summary>
    public decimal? AbsenceRate => Rate(Absences);

    /// <summary>(Présents + retards + partielles + absences JUSTIFIÉES) ÷ renseignées : seules les non justifiées pénalisent.</summary>
    public decimal? AssiduityRate => Rate(Present + Late + PartialAbsence + JustifiedAbsence);

    private decimal? Rate(int numerator) => Recorded == 0 ? null : Math.Round((decimal)numerator / Recorded, 4);

    /// <summary>Part des demi-journées attendues qui sont renseignées (0..1), null si rien n'était attendu.</summary>
    public decimal? Coverage(int expectedHalfDays) =>
        expectedHalfDays <= 0 ? null : Math.Round(Math.Min(1m, (decimal)Recorded / expectedHalfDays), 4);

    public AttendanceCounts Plus(AttendanceCounts other) => new(
        Present + other.Present,
        Late + other.Late,
        PartialAbsence + other.PartialAbsence,
        JustifiedAbsence + other.JustifiedAbsence,
        UnjustifiedAbsence + other.UnjustifiedAbsence,
        LateMinutes + other.LateMinutes);

    public static AttendanceCounts From(IEnumerable<RollBookCell> cells)
    {
        var counts = Zero;

        foreach (var cell in cells)
        {
            counts = counts.Plus(cell.Mark switch
            {
                RollBookMark.Present => new AttendanceCounts(Present: 1),
                RollBookMark.Late => new AttendanceCounts(Late: 1, LateMinutes: cell.LateMinutes),
                RollBookMark.PartialAbsence => new AttendanceCounts(PartialAbsence: 1),
                RollBookMark.JustifiedAbsence => new AttendanceCounts(JustifiedAbsence: 1),
                RollBookMark.UnjustifiedAbsence => new AttendanceCounts(UnjustifiedAbsence: 1),
                _ => Zero
            });
        }

        return counts;
    }

    public static AttendanceCounts Sum(IEnumerable<AttendanceCounts> all) =>
        all.Aggregate(Zero, (total, next) => total.Plus(next));
}
