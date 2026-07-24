using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Commands.GenerateFichePaie;

public record GenerateFichePaieCommand(
    Guid EmployeeContractId,
    int Month,
    int Year,
    decimal HoursWorked = 0,
    decimal TransportAllowance = 0) : IRequest<Guid>;

public class GenerateFichePaieCommandValidator : AbstractValidator<GenerateFichePaieCommand>
{
    public GenerateFichePaieCommandValidator()
    {
        RuleFor(v => v.EmployeeContractId).NotEmpty();
        RuleFor(v => v.Month).InclusiveBetween(1, 12);
        RuleFor(v => v.Year).GreaterThan(2000);
        RuleFor(v => v.HoursWorked).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);
    }
}

public class GenerateFichePaieCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GenerateFichePaieCommand, Guid>
{
    public async Task<Guid> Handle(GenerateFichePaieCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();
        
        // Vérifier si une fiche existe déjà pour ce mois
        var existing = await context.FichePaies
            .AnyAsync(f => f.EmployeeContractId == request.EmployeeContractId && 
                           f.Month == request.Month && 
                           f.Year == request.Year, cancellationToken);
                           
        if (existing)
            throw new BusinessRuleException("Une fiche de paie existe déjà pour ce mois.");

        var contract = await context.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), request.EmployeeContractId);

        // Si le contrat est horaire (Vacataire) et qu'aucune heure n'est fournie, c'est une erreur logique,
        // mais le validateur permet 0, donc ça calculera un salaire de 0.
        // Si c'est un CDI/CDD, le salaire de base est utilisé.
        var fichePaie = PayrollCalculator.CalculateFichePaie(
            schoolId,
            contract.Id,
            request.Month,
            request.Year,
            contract.BaseSalary,
            contract.HourlyRate,
            request.HoursWorked,
            request.TransportAllowance
        );

        context.FichePaies.Add(fichePaie);
        
        // Enregistrement des décaissements associés à la paie (Brut ou Net + Taxes) ?
        // Dans une implémentation avancée, la validation de la paie génèrerait un Disbursement.
        // Pour l'instant, on se contente de générer la fiche.
        
        await context.SaveChangesAsync(cancellationToken);

        return fichePaie.Id;
    }
}
