using FluentValidation;

namespace SamaEcole.Application.Teachers.Commands.ImportTeachers;

/// <summary>
/// Validation de FORME uniquement (le contenu ligne par ligne — e-mail, téléphone, matières... —
/// dépend de l'état en base et vit dans le Handler, comme ImportStudentsCommandValidator).
/// </summary>
public class ImportTeachersCommandValidator : AbstractValidator<ImportTeachersCommand>
{
    public ImportTeachersCommandValidator()
    {
        RuleFor(c => c.FileContent).NotEmpty().WithMessage("Le fichier est vide ou n'a pas pu être lu.");
        RuleFor(c => c.FileName).NotEmpty().WithMessage("Le nom du fichier est requis.");
    }
}
