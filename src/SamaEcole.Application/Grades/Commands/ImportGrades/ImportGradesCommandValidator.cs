using FluentValidation;

namespace SamaEcole.Application.Grades.Commands.ImportGrades;

/// <summary>
/// Validation de FORME uniquement (existence de la classe/matière/trimestre et contenu ligne par ligne
/// dépendent de l'état en base : ils vivent dans le Handler, comme CreateGradeCommandValidator).
/// </summary>
public class ImportGradesCommandValidator : AbstractValidator<ImportGradesCommand>
{
    public ImportGradesCommandValidator()
    {
        RuleFor(c => c.ClassroomId).NotEmpty();
        RuleFor(c => c.SubjectId).NotEmpty();
        RuleFor(c => c.TermId).NotEmpty();
        RuleFor(c => c.EvaluationType).IsInEnum().WithMessage("Type d'évaluation invalide.");
        RuleFor(c => c.FileContent).NotEmpty().WithMessage("Le fichier est vide ou n'a pas pu être lu.");
        RuleFor(c => c.FileName).NotEmpty().WithMessage("Le nom du fichier est requis.");
    }
}
