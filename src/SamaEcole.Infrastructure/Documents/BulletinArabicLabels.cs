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
    public const string BulletinTitle = "بطاقة النتائج";
    public const string SchoolYear = "السنة الدراسية";

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
