using FluentValidation;

namespace SamaEcole.Application.Students.Commands.ImportStudents;

/// <summary>
/// Validation de FORME uniquement (le contenu ligne par ligne — date, classe, genre... — dépend de
/// l'état en base et vit dans le Handler, comme ImportGradesCommandValidator).
/// </summary>
public class ImportStudentsCommandValidator : AbstractValidator<ImportStudentsCommand>
{
    public ImportStudentsCommandValidator()
    {
        RuleFor(c => c.FileContent).NotEmpty().WithMessage("Le fichier est vide ou n'a pas pu être lu.");
        RuleFor(c => c.FileName).NotEmpty().WithMessage("Le nom du fichier est requis.");
    }
}
