using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPayrollTeacherOptions;

/// <summary>
/// GET /finance/employee-contracts/teacher-options — les enseignants proposables dans le sélecteur d'un contrat
/// du personnel (écran Paie).
///
/// Route DÉDIÉE à la Paie plutôt qu'une ouverture de GET /teachers à la Finance : la liste générique porte des données
/// personnelles (téléphone, adresse, naissance…) que la Finance n'a pas à lire (docs/Volume_7_Security.md « Enseignants »).
/// Ici : identifiant, matricule et nom, rien d'autre — de quoi nommer le salarié, pas de quoi le contacter.
/// </summary>
public record GetPayrollTeacherOptionsQuery : IRequest<IReadOnlyList<PayrollTeacherOptionDto>>;

public record PayrollTeacherOptionDto(Guid Id, string Matricule, string FullName);

public class GetPayrollTeacherOptionsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPayrollTeacherOptionsQuery, IReadOnlyList<PayrollTeacherOptionDto>>
{
    /// <summary>Garde-fou : un établissement dépasse très rarement cet effectif enseignant.</summary>
    private const int MaxOptions = 500;

    public async Task<IReadOnlyList<PayrollTeacherOptionDto>> Handle(
        GetPayrollTeacherOptionsQuery request, CancellationToken cancellationToken)
        // Global Query Filter + RLS : uniquement les enseignants de l'école courante, hors supprimés.
        => await dbContext.Teachers.AsNoTracking()
            .OrderBy(t => t.FullName)
            .Take(MaxOptions)
            .Select(t => new PayrollTeacherOptionDto(t.Id, t.Matricule, t.FullName))
            .ToListAsync(cancellationToken);
}
