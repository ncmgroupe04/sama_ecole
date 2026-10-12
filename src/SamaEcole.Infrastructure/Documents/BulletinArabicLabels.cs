using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Traductions arabes des libellés FIXES du bulletin (titre, décisions du conseil, mentions
/// disciplinaires) — le seul contenu bilingue qui ne dépend d'AUCUNE saisie d'école, contrairement au
/// nom d'une matière (<see cref="Domain.Entities.Subject.NameAr"/>, saisi par l'école elle-même).
///
/// ⚠️ CES TRADUCTIONS DOIVENT ÊTRE RELUES PAR UNE PERSONNE LISANT L'ARABE avant toute mise en
/// production — elles n'ont pas été vérifiées par un locuteur natif ou un professionnel de la langue.
/// </summary>
public static class BulletinArabicLabels
{
    public const string BulletinTitle = "كشف النقاط";
    public const string SchoolYear = "السنة الدراسية";

    // Bandeau officiel et libellés du bulletin à deux sections (ReportCardDocument.Sections.cs).
    public const string Republic = "جمهورية السنغال";
    public const string Motto = "شعب واحد – هدف واحد – إيمان واحد";
    public const string Ministry = "وزارة التربية الوطنية";
    public const string FrenchSection = "القسم الفرنسي";
    public const string ArabicSection = "القسم العربي";
    public const string FrenchSectionAverage = "معدل القسم الفرنسي";
    public const string ArabicSectionAverage = "معدل القسم العربي";
    public const string GeneralAverage = "المعدل العام";
    public const string Attendance = "المواظبة";
    public const string Subject = "المادة";
    public const string Coefficient = "المعامل";
    public const string Average = "المعدل";
    public const string Rank = "الرتبة";
    public const string Appreciation = "التقدير";
    public const string Points = "النقاط";
    public const string Exempt = "معفى(ة)";
    public const string CouncilObservations = "ملاحظات مجلس الأقسام";
    public const string SchoolHead = "مدير المؤسسة";

    /// <summary>
    /// Appréciation par matière (vocabulaire fixe des bulletins sénégalais, voir SubjectAppreciationScale),
    /// indexée par le libellé FRANÇAIS exact produit par ce barème.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Appreciations = new Dictionary<string, string>
    {
        ["Très Bien"] = "جيد جدا",
        ["Bon Travail"] = "عمل جيد",
        ["Assez Bien"] = "حسن",
        ["Moyen"] = "متوسط",
        ["Insuffisant"] = "غير كاف",
        ["Faible"] = "ضعيف"
    };

    public static readonly IReadOnlyDictionary<CouncilDecision, string> CouncilDecisions = new Dictionary<CouncilDecision, string>
    {
        [CouncilDecision.Admitted] = "ناجح(ة) إلى القسم الأعلى",
        [CouncilDecision.AllowedToRepeat] = "مسموح له(ا) بالإعادة",
        [CouncilDecision.Excluded] = "مطرود(ة)"
    };

    public static readonly IReadOnlyDictionary<DisciplinaryMention, string> DisciplinaryMentions = new Dictionary<DisciplinaryMention, string>
    {
        [DisciplinaryMention.Blame] = "توبيخ",
        [DisciplinaryMention.Avertissement] = "إنذار",
        [DisciplinaryMention.TableauHonneur] = "لوحة الشرف",
        [DisciplinaryMention.Encouragements] = "تشجيع",
        [DisciplinaryMention.Felicitations] = "تهنئة"
    };
}
