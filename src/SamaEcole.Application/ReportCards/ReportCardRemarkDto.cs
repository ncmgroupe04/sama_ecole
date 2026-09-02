using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.ReportCards;

/// <summary>
/// Ce que le conseil des professeurs a saisi pour un élève, un trimestre donné — partagé par
/// GetReportCardRemarkQuery (lecture, pour préremplir l'écran de saisie) et
/// UpsertReportCardRemarkCommand (écriture). <see cref="DisciplinaryMention"/> et
/// <see cref="Observations"/> sont null tant que rien n'a été saisi — jamais une valeur par défaut
/// inventée (même parti pris que Grade côté notes).
/// </summary>
public record ReportCardRemarkDto(
    Guid StudentId,
    Guid TermId,
    DisciplinaryMention? DisciplinaryMention,
    CouncilDecision? CouncilDecision,
    string? Observations,

    // Distinction que le moteur du bulletin A5 imprimerait FAUTE de saisie du conseil — exactement le
    // « ?? DisciplinaryMentionPolicy.Suggest(...) » de ReportCardDataService. Remontée pour que l'écran
    // de saisie la pré-sélectionne : le conseil valide d'un clic (Enregistrer) ou choisit autre chose.
    // Purement INDICATIVE — jamais persistée en tant que telle. Null quand aucune n'est proposée
    // (moyenne générale sous le premier seuil, ou aucune note), et null aussi dans la réponse de
    // l'upsert, qui ne la recalcule pas (l'écran l'a déjà, et la valeur retenue est celle qu'il envoie).
    DisciplinaryMention? SuggestedDisciplinaryMention = null,

    // Moyenne générale du trimestre au moment de la lecture — sert au libellé de l'écran (« Proposé
    // d'après la moyenne 16 : Félicitations »). Null tant que rien n'est noté.
    decimal? GeneralAverage = null);
