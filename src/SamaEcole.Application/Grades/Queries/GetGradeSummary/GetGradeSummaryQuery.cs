using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetGradeSummary;

/// <summary>
/// GET /api/v1/grades/calculate?studentId=&amp;termId= — ticket JGK-G02. Calcule, pour un élève et un
/// trimestre : la moyenne de chaque matière notée, la moyenne générale pondérée par les coefficients
/// (Volume 1 §8.3 : « total des coefficients, total des points, moyenne générale »), et la mention qui
/// en découle (§8.4). Lecture seule, aucune écriture — un recalcul à la demande, jamais persisté ici :
/// la persistance (bulletin figé) est l'affaire de JGK-G03.
/// </summary>
public record GetGradeSummaryQuery(Guid StudentId, Guid TermId) : IRequest<GradeSummaryDto>;

/// <summary>
/// Moyenne d'une matière pour ce trimestre. <see cref="Devoir1"/>/<see cref="Devoir2"/>/
/// <see cref="Composition"/> sont null si ce type d'évaluation n'a pas encore été saisi — la moyenne
/// se calcule alors sur ce qui existe, sans exiger que les trois soient renseignés (la saisie progresse
/// au fil du trimestre). <see cref="DevoirAverage"/> est la moyenne de Devoir1/Devoir2
/// (GradeCalculator.DevoirAverage) — c'est elle qui s'imprime dans la colonne « Devoir » du bulletin.
/// </summary>
public record SubjectGradeDto(
    Guid SubjectId,
    string SubjectName,
    decimal? Devoir1,
    decimal? Devoir2,
    decimal? Composition,
    decimal? DevoirAverage,
    decimal Average,
    decimal Coefficient,
    decimal WeightedPoints,

    // Barème de la ligne — la colonne « Sur » du bulletin. Toujours résolu (jamais null) : la valeur
    // fixée sur la matière, ou à défaut celle du cycle de la classe (GradeCalculator.EffectiveMaxScore).
    // <see cref="Average"/> reste exprimée SUR CE BARÈME, brute : c'est la note que l'école a saisie et
    // celle qu'imprime le bulletin. <see cref="WeightedPoints"/>, lui, est déjà ramené au barème du
    // bulletin — sans quoi une ligne /60 pèserait trois fois une ligne /20 dans la moyenne générale.
    decimal MaxScore = 20m,

    // Domaine parent d'une grille APC (« Lang & Com. » pour « P. Alphabétique »). Null pour une matière
    // de premier niveau — le cas de toute matière du secondaire.
    Guid? ParentSubjectId = null,
    string? ParentSubjectName = null);

/// <summary>
/// Matière OBLIGATOIRE dont l'élève est dispensé (avec motif), portée par le résumé pour que le bulletin la
/// marque « Dispensé(e) ». Elle n'entre ni dans <see cref="GradeSummaryDto.Subjects"/> ni dans les totaux.
/// <see cref="Coefficient"/> est le coefficient EFFECTIF (surcharges comprises ; 1 au primaire) : il
/// s'imprime barré. Le MOTIF n'est volontairement pas ici — il peut être médical et ne figure sur aucun document.
/// </summary>
public record ExemptSubjectDto(Guid SubjectId, string SubjectName, decimal Coefficient);

/// <summary>
/// <see cref="Mention"/> est null tant qu'aucune matière n'est notée (rien à qualifier), ou si la
/// moyenne générale n'atteint le seuil d'aucune mention configurée.
/// </summary>
public record GradeSummaryDto(
    Guid StudentId,
    Guid TermId,
    IReadOnlyList<SubjectGradeDto> Subjects,
    decimal TotalCoefficients,
    decimal TotalPoints,
    decimal GeneralAverage,
    string? Mention,
    IReadOnlyList<ExemptSubjectDto>? ExemptSubjects = null);
