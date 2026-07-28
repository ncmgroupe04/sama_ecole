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
    string? Observations);
