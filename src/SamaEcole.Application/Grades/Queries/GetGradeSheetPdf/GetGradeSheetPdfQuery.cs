using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetGradeSheetPdf;

/// <summary>
/// GET /api/v1/grades/sheet/print — bouton « Imprimer la fiche papier » de l'écran de saisie. Génère une
/// fiche VIERGE (aucune note pré-remplie) pour une classe, une matière, un trimestre et une évaluation :
/// les élèves y figurent par ordre alphabétique, avec des cases vides « Note » et « Appréciation » à
/// remplir au stylo. Lecture seule (AGENTS.md règle #7) — la saisie reste CreateGradeCommand.
///
/// Distincte de <see cref="GetGradeSheetExcel.GetGradeSheetExcelQuery"/> : la feuille Excel est pré-remplie et
/// sert à RÉIMPORTER ; la fiche papier est vierge et sert à noter dans la salle.
/// </summary>
public record GetGradeSheetPdfQuery(Guid ClassroomId, Guid SubjectId, Guid TermId, EvaluationType EvaluationType)
    : IRequest<GradeSheetPdfResult>;

public record GradeSheetPdfResult(byte[] Content, string FileName);

/// <summary>Tout ce que la fiche imprime — le générateur n'interroge jamais la base.</summary>
public record GradeSheetPdfDto(
    string SchoolName,
    string SchoolYearLabel,
    string TermLabel,
    string ClassroomName,
    string SubjectName,
    string EvaluationLabel,
    decimal MaxScore,
    IReadOnlyList<GradeSheetPdfStudent> Students);

public record GradeSheetPdfStudent(string Matricule, string FullName);
