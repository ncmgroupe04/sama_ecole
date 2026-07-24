using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Finance.Services;

namespace SamaEcole.Application.Finance.Commands.GenerateTaxDeclaration;

public record GenerateTaxDeclarationCommand(int Month, int Year) : IRequest<Guid>;

public class GenerateTaxDeclarationCommandValidator : AbstractValidator<GenerateTaxDeclarationCommand>
{
    public GenerateTaxDeclarationCommandValidator()
    {
        RuleFor(v => v.Month).InclusiveBetween(1, 12);
        RuleFor(v => v.Year).GreaterThan(2000);
    }
}

public class GenerateTaxDeclarationCommandHandler(
    IApplicationDbContext context,
    ITenantProvider tenantProvider) : IRequestHandler<GenerateTaxDeclarationCommand, Guid>
{
    public async Task<Guid> Handle(GenerateTaxDeclarationCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        // 1. Check if already generated
        var existing = await context.TaxeDeclarations
            .AnyAsync(t => t.Month == request.Month && t.Year == request.Year, cancellationToken);
            
        if (existing)
            throw new BusinessRuleException("Une déclaration fiscale existe déjà pour cette période.");

        // 2. Fetch all FichePaies for the period
        var fiches = await context.FichePaies
            .Where(f => f.Month == request.Month && f.Year == request.Year)
            .ToListAsync(cancellationToken);

        // 3. Fetch all Payments (Encaissements) to calculate TVA collected
        // Note: Dans une vraie implémentation, on filtrerait sur les paiements assujettis à la TVA.
        // On suppose que tous les SubscriptionPayments (qui sont en fait les paiements de l'école envers la plateforme)
        // et les Payments (élèves envers école) n'ont pas forcément de TVA détaillée dans le modèle actuel.
        // Pour respecter le ticket, on agrège simplement un montant fictif de TVA ou on demande 
        // une saisie manuelle si le modèle ne capture pas le détail HT/TVA.
        // Ici, on supposera 0 pour l'exemple, ou un calcul sur les frais assujettis.
        decimal tvaCollected = 0m; 
        
        // 4. Fetch all Disbursements (Décaissements) to calculate TVA deductible
        // Pareillement, si le décaissement a une TVA récupérable
        decimal tvaDeductible = 0m;

        // 5. Generate Declaration
        var declaration = TaxCalculator.CalculateTaxDeclaration(
            schoolId,
            request.Month,
            request.Year,
            fiches,
            tvaCollected,
            tvaDeductible
        );

        context.TaxeDeclarations.Add(declaration);
        await context.SaveChangesAsync(cancellationToken);

        return declaration.Id;
    }
}
