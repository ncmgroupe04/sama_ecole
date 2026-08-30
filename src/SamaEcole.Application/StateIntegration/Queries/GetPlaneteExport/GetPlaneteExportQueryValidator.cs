using FluentValidation;

namespace SamaEcole.Application.StateIntegration.Queries.GetPlaneteExport;

public class GetPlaneteExportQueryValidator : AbstractValidator<GetPlaneteExportQuery>
{
    public GetPlaneteExportQueryValidator()
    {
        RuleFor(q => q.SchoolYearId)
            .NotEmpty()
            .WithMessage("L'année scolaire est obligatoire : un export Planète porte toujours sur un exercice précis.");

        RuleFor(q => q.Format)
            .IsInEnum()
            .WithMessage("Format d'export inconnu (attendu : Json ou Csv).");

        // Un ClassroomId FOURNI mais vide est une erreur de l'appelant, pas un « toute l'école » :
        // laisser passer Guid.Empty produirait silencieusement un export global là où l'utilisateur
        // croyait n'extraire qu'une classe — exactement l'inverse de son intention.
        RuleFor(q => q.ClassroomId)
            .NotEqual(Guid.Empty)
            .When(q => q.ClassroomId.HasValue)
            .WithMessage("Identifiant de classe invalide. Omettez le paramètre pour exporter tout l'établissement.");
    }
}
