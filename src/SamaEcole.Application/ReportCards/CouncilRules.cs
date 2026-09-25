using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Grades;
using SamaEcole.Application.Grades.Queries.GetGradeSummary;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Seuils du conseil de classe d'un établissement (Évolution N°7), tous sur la RÉFÉRENCE /20 — transposés au
/// barème du bulletin au moment de juger, pour qu'un primaire /10 ne soit pas jugé sur des seuils /20.
/// Configurables par le Directeur (SchoolSettings), défauts de la spécification MEN (SchoolSettingsDefaults).
/// </summary>
public sealed record CouncilRules(
    decimal FelicitationsMin,
    decimal HonorRollMin,
    decimal EncouragementsMin,
    decimal EliminatoryGrade,
    decimal PromotionMin,
    decimal RepeatMin)
{
    public const decimal Reference = 20m;

    public static readonly CouncilRules Default = new(
        SchoolSettingsDefaults.CouncilFelicitationsMin,
        SchoolSettingsDefaults.CouncilHonorRollMin,
        SchoolSettingsDefaults.CouncilEncouragementsMin,
        SchoolSettingsDefaults.CouncilEliminatoryGrade,
        SchoolSettingsDefaults.CouncilPromotionMin,
        SchoolSettingsDefaults.CouncilRepeatMin);

    public static CouncilRules From(SchoolSettings? settings) => settings is null
        ? Default
        : new(settings.CouncilFelicitationsMin, settings.CouncilHonorRollMin, settings.CouncilEncouragementsMin,
            settings.CouncilEliminatoryGrade, settings.CouncilPromotionMin, settings.CouncilRepeatMin);

    /// <summary>Les règles de l'école COURANTE (Global Query Filter + RLS), à défaut celles de la spécification.</summary>
    public static async Task<CouncilRules> ResolveAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
        => From(await dbContext.SchoolSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken));

    /// <summary>Une valeur exprimée sur <paramref name="scale"/> ramenée sur /20.</summary>
    public static decimal ToReference(decimal value, decimal scale) => scale <= 0 ? 0 : value * Reference / scale;

    /// <summary>
    /// Vrai si l'une des matières notées a une moyenne ÉLIMINATOIRE (strictement sous le seuil), chacune jugée sur
    /// son propre barème (grilles APC : /40, /60…) ramené à /20.
    /// </summary>
    public bool HasEliminatoryGrade(IEnumerable<SubjectGradeDto> subjects)
        => subjects.Any(s => ToReference(s.Average, s.MaxScore) < EliminatoryGrade);

    /// <summary>
    /// Distinction récompensant un résultat — la moitié HAUTE de la rangée seulement (voir DisciplinaryMentionPolicy :
    /// Blâme et Avertissement ne se déduisent jamais d'une moyenne). Ordre : Félicitations, puis Tableau d'Honneur
    /// (sans note éliminatoire), puis Encouragements. Null sans note, ou sous tous les seuils.
    /// </summary>
    public DisciplinaryMention? SuggestDistinction(
        decimal generalAverage, decimal gradingScale, bool hasGrades, bool hasEliminatoryGrade)
    {
        if (!hasGrades)
        {
            return null;
        }

        var average = ToReference(generalAverage, gradingScale);

        if (average >= FelicitationsMin) return DisciplinaryMention.Felicitations;
        if (average >= HonorRollMin && !hasEliminatoryGrade) return DisciplinaryMention.TableauHonneur;
        if (average >= EncouragementsMin) return DisciplinaryMention.Encouragements;
        return null;
    }

    /// <summary>
    /// Proposition de décision de fin d'année d'après la moyenne ANNUELLE : Admis en classe supérieure, Autorisé à
    /// redoubler, ou Exclus. Une PROPOSITION : elle ne s'imprime comme décision que si le conseil l'a retenue
    /// (ApplyCouncilDecisionProposalsCommand ou saisie). Null sans moyenne annuelle.
    /// </summary>
    public CouncilDecision? SuggestDecision(decimal? annualAverage, decimal gradingScale)
    {
        if (annualAverage is not { } value)
        {
            return null;
        }

        var average = ToReference(value, gradingScale);
        if (average >= PromotionMin) return CouncilDecision.Admitted;
        if (average >= RepeatMin) return CouncilDecision.AllowedToRepeat;
        return CouncilDecision.Excluded;
    }

    /// <summary>Cohérence des seuils : ordre croissant attendu, tous dans [0 ; 20].</summary>
    public IEnumerable<string> Validate()
    {
        foreach (var (name, value) in new[]
                 {
                     ("Félicitations", FelicitationsMin), ("Tableau d'honneur", HonorRollMin),
                     ("Encouragements", EncouragementsMin), ("Note éliminatoire", EliminatoryGrade),
                     ("Passage", PromotionMin), ("Redoublement", RepeatMin)
                 })
        {
            if (value < 0 || value > Reference)
            {
                yield return $"Le seuil « {name} » doit être compris entre 0 et 20.";
            }
        }

        if (FelicitationsMin < HonorRollMin || FelicitationsMin < EncouragementsMin)
        {
            yield return "Les Félicitations doivent exiger une moyenne au moins égale au Tableau d'honneur et aux Encouragements.";
        }

        if (RepeatMin > PromotionMin)
        {
            yield return "Le seuil de redoublement ne peut pas dépasser le seuil de passage.";
        }
    }
}
