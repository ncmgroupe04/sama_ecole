using FluentAssertions;
using SamaEcole.Application.Classrooms;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Xunit;

namespace SamaEcole.UnitTests.Classrooms;

/// <summary>
/// Moteur de délibération des classes PASSERELLES / ACCÉLÉRÉES : une année scolaire qui valide DEUX
/// niveaux (« CI-CP », « 6e-5e », parcours d'intégration des élèves venus des écoles coraniques).
///
/// Ce qui est réellement en jeu : un élève admis en classe passerelle dont l'historique ne retiendrait
/// qu'un seul niveau redémarrerait l'année suivante au niveau qu'il vient précisément de sauter — le
/// dispositif serait annulé par sa propre délibération. Symétriquement, l'option étant facultative, une
/// classe ordinaire ne doit RIEN changer à sa délibération d'origine : c'est l'immense majorité des cas.
/// </summary>
public class ClassroomPromotionTests
{
    private static Classroom Classroom(
        string name = "CM2 A",
        CycleType cycle = CycleType.Primaire,
        bool isAccelerated = false,
        string? targetLevel = null) => new()
    {
        Name = name,
        Level = cycle.ToString(),
        Cycle = cycle,
        IsAccelerated = isAccelerated,
        TargetLevel = targetLevel
    };

    // ─────────────────────────────────────────────────── Double niveau (le cœur du dispositif)

    [Fact]
    public void An_Admitted_Student_Of_An_Accelerated_Class_Validates_Both_Levels()
    {
        var passerelle = Classroom("CI-CP", CycleType.Primaire, isAccelerated: true, targetLevel: "CP");

        var validated = ClassroomPromotion.ValidatedLevels(passerelle, CouncilDecision.Admitted);

        // Le niveau courant est lu sur le NOM (« CI-CP » → CI) et le niveau cible sur la configuration
        // de la classe : l'ordre est l'ordre pédagogique, pas un ordre d'insertion.
        validated.Should().Equal("CI", "CP");
    }

    [Fact]
    public void An_Admitted_Student_Of_An_Accelerated_Secondary_Class_Validates_Both_Levels()
    {
        var passerelle = Classroom("6e-5e B", CycleType.College, isAccelerated: true, targetLevel: "Cinquième");

        ClassroomPromotion.ValidatedLevels(passerelle, CouncilDecision.Admitted)
            .Should().Equal("Sixième", "Cinquième");
    }

    [Fact]
    public void An_Admitted_Student_Of_An_Ordinary_Class_Still_Validates_A_Single_Level()
    {
        // Non-régression : l'option ne doit toucher à RIEN pour une classe ordinaire.
        ClassroomPromotion.ValidatedLevels(Classroom("CM2 A"), CouncilDecision.Admitted)
            .Should().Equal("CM2");
    }

    // ─────────────────────────────────────────────────── La décision du conseil reste souveraine

    [Theory]
    [InlineData(CouncilDecision.AllowedToRepeat)]
    [InlineData(CouncilDecision.Excluded)]
    [InlineData(null)]
    public void No_Level_Is_Validated_Without_An_Admission_Even_In_An_Accelerated_Class(CouncilDecision? decision)
    {
        // Une passerelle accélère un parcours, elle ne dispense pas de la délibération : un élève non
        // admis (ou pas encore délibéré) ne valide rien — surtout pas deux niveaux d'un coup.
        var passerelle = Classroom("CI-CP", CycleType.Primaire, isAccelerated: true, targetLevel: "CP");

        ClassroomPromotion.ValidatedLevels(passerelle, decision).Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────── Configuration incomplète ou contradictoire

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void An_Accelerated_Class_Without_A_Target_Level_Falls_Back_To_A_Single_Level(string? targetLevel)
    {
        // Case cochée mais second niveau jamais renseigné (donnée héritée, appel API direct) : on
        // délibère comme une classe ordinaire plutôt que d'inventer un second niveau.
        var incomplete = Classroom("CI-CP", CycleType.Primaire, isAccelerated: true, targetLevel: targetLevel);

        ClassroomPromotion.ValidatedLevels(incomplete, CouncilDecision.Admitted).Should().Equal("CI");
    }

    [Fact]
    public void A_Target_Level_Equal_To_The_Current_One_Is_Not_Validated_Twice()
    {
        // La validation de saisie l'interdit déjà ; si la donnée passe quand même (import, API directe),
        // l'historique ne doit pas porter deux fois le même niveau.
        var contradictory = Classroom("CM2 A", CycleType.Primaire, isAccelerated: true, targetLevel: "CM2");

        ClassroomPromotion.ValidatedLevels(contradictory, CouncilDecision.Admitted).Should().Equal("CM2");
    }

    [Fact]
    public void A_Classroom_Named_Outside_Any_Nomenclature_Falls_Back_To_Its_Own_Name()
    {
        // Aucune nomenclature ne couvre « Groupe Coranique 1 » : l'historique garde le nom saisi par
        // l'école plutôt qu'un niveau vide ou inventé.
        var unusual = Classroom("Groupe Coranique 1", CycleType.Primaire, isAccelerated: true, targetLevel: "CP");

        ClassroomPromotion.ValidatedLevels(unusual, CouncilDecision.Admitted)
            .Should().Equal("Groupe Coranique 1", "CP");
    }

    // ─────────────────────────────────────────────────── Mentions imprimées

    [Fact]
    public void The_Accelerated_Mention_Names_Both_Levels()
    {
        var passerelle = Classroom("CI-CP", CycleType.Primaire, isAccelerated: true, targetLevel: "CP");

        ClassroomPromotion.AcceleratedPathLabel(passerelle)
            .Should().Be("Cursus Accéléré Passerelle — CI → CP");
    }

    [Fact]
    public void An_Ordinary_Class_Carries_No_Mention_At_All()
    {
        // Null, et pas une chaîne vide : c'est ce null qui fait que le bulletin et le PV n'insèrent
        // même pas une ligne vide, et restent au gabarit de la référence visuelle.
        ClassroomPromotion.AcceleratedPathLabel(Classroom("CM2 A")).Should().BeNull();
    }

    [Fact]
    public void An_Accelerated_Class_Without_A_Target_Still_Carries_The_Bare_Mention()
    {
        ClassroomPromotion.AcceleratedPathLabel(Classroom("CI-CP", isAccelerated: true))
            .Should().Be("Cursus Accéléré Passerelle");
    }

    // ─────────────────────────────────────────────────── Libellé « Classe » des pièces imprimées

    [Fact]
    public void A_Receipt_Names_The_Bridge_Programme_Next_To_The_Class()
    {
        // Le reçu est la seule pièce que le tuteur conserve : elle doit dire que l'année réglée en
        // couvre deux (reçu d'inscription ET reçu de caisse, même formulation).
        ClassroomPromotion.DisplayName("CI-CP", isAccelerated: true)
            .Should().Be("CI-CP (Cursus Accéléré Passerelle)");
    }

    [Fact]
    public void A_Receipt_For_An_Ordinary_Class_Keeps_The_Plain_Class_Name()
    {
        // Non-régression sur la référence de design du reçu (AGENTS.md règle #12) : pas un caractère
        // de plus pour une classe ordinaire.
        ClassroomPromotion.DisplayName("CM2 A", isAccelerated: false).Should().Be("CM2 A");
    }

    // ─────────────────────────────────────────────────── Normalisation à l'écriture

    [Fact]
    public void Unchecking_The_Option_Clears_The_Target_Level()
    {
        // Sans cet effacement, une classe redevenue ordinaire garderait un niveau cible invisible à
        // l'écran, prêt à ressortir si la case était recochée.
        ClassroomPromotion.NormalizeTargetLevel(isAccelerated: false, targetLevel: "CP").Should().BeNull();
    }

    [Fact]
    public void A_Kept_Target_Level_Is_Trimmed()
    {
        ClassroomPromotion.NormalizeTargetLevel(isAccelerated: true, targetLevel: "  CP  ").Should().Be("CP");
    }
}
