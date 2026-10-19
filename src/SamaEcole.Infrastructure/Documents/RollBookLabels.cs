using SamaEcole.Application.Features.Schedules;
using SamaEcole.Application.RollBook;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Libellés FIXES du cahier de roulement imprimé, en français et en arabe (module Franco-Arabe, Bloc 5c). Même
/// réserve que <see cref="BulletinArabicLabels"/> et <see cref="ExamArabicLabels"/> : ⚠️ LES TRADUCTIONS ARABES SONT À
/// FAIRE RELIRE PAR UNE PERSONNE LISANT L'ARABE avant mise en production — notamment l'intitulé du registre, les noms
/// des niveaux tels que les écrit l'administration, et les abréviations d'une lettre des cases (ح ت ع غ).
///
/// Les noms de mois et de jours sont écrits ICI, jamais pris à la culture système : « ar-SA » donnerait le calendrier
/// hégirien, et une image de conteneur sans données de culture donnerait des noms vides.
/// </summary>
public static class RollBookLabels
{
    // ------------------------------------------------------------------------------------------- Titre

    public const string TitleFr = "CAHIER DE ROULEMENT";
    public const string TitleAr = "سجل الحضور والغياب";
    public const string RecapFr = "RÉCAPITULATIF PÉRIODIQUE";
    public const string RecapAr = "الملخص الدوري";
    public const string LevelSynthesisFr = "SYNTHÈSE DU NIVEAU";
    public const string LevelSynthesisAr = "ملخص المستوى";

    public const string SchoolYearAr = "السنة الدراسية";
    public const string ClassAr = "القسم";
    public const string LevelAr = "المستوى";
    public const string MonthWordAr = "شهر";
    public const string PeriodAr = "الفترة";
    public const string StudentAr = "التلميذ";
    public const string AbsentsAr = "الغائبون";
    public const string PageAr = "صفحة";

    // Inspection / établissement : lignes à compléter à la main quand la version arabe n'est pas renseignée.
    public const string InspectionAcademieAr = "مفتشية الأكاديمية";
    public const string InspectionEducationAr = "مفتشية التربية والتكوين";
    public const string SchoolAr = "المؤسسة";

    // ---------------------------------------------------------------------------------- Cases et légende

    public const string PresentFr = "Présent";
    public const string PresentAr = "حاضر";
    public const string LateFr = "Retard";
    public const string LateAr = "متأخر";
    public const string PartialFr = "Absence partielle";
    public const string PartialAr = "غياب جزئي";
    public const string JustifiedFr = "Absent justifié";
    public const string JustifiedAr = "غائب بعذر";
    public const string UnjustifiedFr = "Absent non justifié";
    public const string UnjustifiedAr = "غائب بدون عذر";

    public const string MorningShortFr = "M";
    public const string AfternoonShortFr = "S";
    public const string MorningShortAr = "ص";
    public const string AfternoonShortAr = "م";

    // ------------------------------------------------------------------------------ Totaux et signatures

    public const string ExpectedFr = "Demi-journées attendues";
    public const string ExpectedAr = "الفترات المنتظرة";
    public const string RecordedFr = "Demi-journées renseignées";
    public const string RecordedAr = "الفترات المسجلة";
    public const string PresenceRateFr = "Taux de présence";
    public const string PresenceRateAr = "نسبة الحضور";
    public const string AbsenceRateFr = "Taux d'absence";
    public const string AbsenceRateAr = "نسبة الغياب";
    public const string AssiduityRateFr = "Taux d'assiduité";
    public const string AssiduityRateAr = "نسبة المواظبة";
    public const string CoverageFr = "Couverture";
    public const string CoverageAr = "نسبة التسجيل";
    public const string TotalFr = "Total";
    public const string TotalAr = "المجموع";
    public const string ClassesFr = "Classes";
    public const string ClassesAr = "الأقسام";
    public const string StudentsFr = "Élèves";
    public const string StudentsAr = "التلاميذ";

    public const string TeacherFr = "Le titulaire de la classe";
    public const string TeacherAr = "أستاذ القسم";
    public const string SchoolHeadFr = "Le Chef d'Établissement";
    public const string SchoolHeadAr = "مدير المؤسسة";
    public const string StampFr = "Cachet de l'établissement";
    public const string StampAr = "ختم المؤسسة";

    // ------------------------------------------------------------------------------------ Mois et jours

    private static readonly string[] FrenchMonths =
    [
        "janvier", "février", "mars", "avril", "mai", "juin", "juillet", "août", "septembre", "octobre", "novembre", "décembre"
    ];

    // Noms standard des mois grégoriens en arabe ; jamais le calendrier hégirien. À confirmer à la relecture (usage local).
    private static readonly string[] ArabicMonths =
    [
        "يناير", "فبراير", "مارس", "أبريل", "مايو", "يونيو", "يوليو", "أغسطس", "سبتمبر", "أكتوبر", "نوفمبر", "ديسمبر"
    ];

    // Index = (int)DayOfWeek : dimanche → samedi.
    private static readonly string[] FrenchWeekdayInitials = ["D", "L", "M", "M", "J", "V", "S"];
    private static readonly string[] ArabicWeekdayInitials = ["ح", "ن", "ث", "ر", "خ", "ج", "س"];

    public static string MonthFr(int month) => FrenchMonths[month - 1];

    public static string MonthAr(int month) => ArabicMonths[month - 1];

    public static string MonthTitleFr(DateOnly date) =>
        $"{char.ToUpperInvariant(FrenchMonths[date.Month - 1][0])}{FrenchMonths[date.Month - 1][1..]} {date.Year}";

    public static string MonthTitleAr(DateOnly date) => $"{ArabicMonths[date.Month - 1]} {date.Year}";

    public static string WeekdayInitial(DayOfWeek day, bool arabic) =>
        (arabic ? ArabicWeekdayInitials : FrenchWeekdayInitials)[(int)day];

    // ------------------------------------------------------------------------------------------- Niveaux

    /// <summary>
    /// Le niveau en arabe, pour les niveaux de la nomenclature (<c>ClassroomGradeLevels</c>). Null pour un niveau hors
    /// nomenclature : le document n'invente aucune traduction, il n'imprime alors que le français.
    /// </summary>
    public static string? Level(string? frenchLevel) => frenchLevel?.Trim() switch
    {
        "TPS" => "القسم التحضيري الأول",
        "PS" => "القسم التحضيري الثاني",
        "MS" => "القسم التحضيري الثالث",
        "GS" => "القسم التحضيري الرابع",
        "CI" => "السنة الأولى ابتدائي",
        "CP" => "السنة الثانية ابتدائي",
        "CE1" => "السنة الثالثة ابتدائي",
        "CE2" => "السنة الرابعة ابتدائي",
        "CM1" => "السنة الخامسة ابتدائي",
        "CM2" => "السنة السادسة ابتدائي",
        "Sixième" => "السنة الأولى متوسط",
        "Cinquième" => "السنة الثانية متوسط",
        "Quatrième" => "السنة الثالثة متوسط",
        "Troisième" => "السنة الرابعة متوسط",
        "Seconde" => "السنة الأولى ثانوي",
        "Première" => "السنة الثانية ثانوي",
        "Terminale" => "السنة الثالثة ثانوي",
        _ => null
    };

    // ------------------------------------------------------------------------------ Alternance FR / AR

    /// <summary>Une ligne d'information sur l'alternance des sections, ou null quand il n'y a rien à constater.</summary>
    public static (string French, string Arabic)? Pattern(SectionPattern? pattern) => pattern switch
    {
        SectionPattern.MorningFrenchAfternoonArabic =>
            ("Alternance : le matin en français, l'après-midi en arabe", "التناوب: الصباح بالفرنسية والمساء بالعربية"),
        SectionPattern.MorningArabicAfternoonFrench =>
            ("Alternance : le matin en arabe, l'après-midi en français", "التناوب: الصباح بالعربية والمساء بالفرنسية"),
        SectionPattern.AlternatingDays =>
            ("Alternance par jours : français et arabe un jour sur deux", "التناوب حسب الأيام: الفرنسية والعربية يوما بيوم"),
        SectionPattern.Mixed =>
            ("Les deux sections se partagent les demi-journées", "تتقاسم الفرنسية والعربية نفس الفترات"),
        _ => null
    };

    // ------------------------------------------------------------------------------------------- Cases

    /// <summary>Lettre imprimée dans la case : une seule, pour tenir dans une colonne de 10 points.</summary>
    public static string Glyph(RollBookMark mark, bool arabic) => (mark, arabic) switch
    {
        (RollBookMark.Present, false) => "P",
        (RollBookMark.Late, false) => "R",
        (RollBookMark.PartialAbsence, _) => "½",
        (RollBookMark.JustifiedAbsence, false) => "J",
        (RollBookMark.UnjustifiedAbsence, false) => "A",
        (RollBookMark.Present, true) => "ح",
        (RollBookMark.Late, true) => "ت",
        (RollBookMark.JustifiedAbsence, true) => "ع",
        (RollBookMark.UnjustifiedAbsence, true) => "غ",
        _ => ""
    };

    /// <summary>« 87,5 % » (virgule française) ; « — » quand rien n'est renseigné : jamais un faux 100 %.</summary>
    public static string Percent(decimal? rate) =>
        rate is null
            ? "—"
            : $"{(Math.Round(rate.Value * 100, 1)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',')} %";
}
