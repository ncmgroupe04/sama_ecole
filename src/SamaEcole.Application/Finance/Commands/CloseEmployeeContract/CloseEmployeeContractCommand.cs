using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.CloseEmployeeContract;

/// <summary>
/// POST /finance/employee-contracts/{id}/close — Volume 1 §14.1 : « un contrat n'est jamais supprimé
/// physiquement… il est clôturé à une date, et reste consultable pour l'historique de paie et les
/// attestations ». Ne supprime donc RIEN (AGENTS.md règle #6) : pose <see cref="EmployeeContract.EndDate"/>.
///
/// Une fois clôturé, un contrat ne peut plus générer de nouvelle fiche de paie (voir
/// GenerateFichePaieCommandHandler) — la dernière fiche due doit être générée AVANT cet appel, et la
/// clôture ne peut pas être annulée (pas de « rouvrir » : si la personne revient, un nouveau contrat se
/// crée, l'index unique TeacherId/UserId l'autorise désormais puisqu'il exclut les contrats clôturés).
/// </summary>
public record CloseEmployeeContractCommand(
    Guid ContractId, DateOnly EndDate, string Reason, uint RowVersion) : IRequest<CloseEmployeeContractResult>, IAuditableRequest;

public record CloseEmployeeContractResult(Guid ContractId, DateOnly EndDate);

public class CloseEmployeeContractCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<CloseEmployeeContractCommand, CloseEmployeeContractResult>
{
    public async Task<CloseEmployeeContractResult> Handle(
        CloseEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.ContractId} introuvable.");

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException("Ce contrat est déjà clôturé.");
        }

        dbContext.SetOriginalConcurrencyToken(contract, request.RowVersion);

        contract.EndDate = request.EndDate;

        dbContext.EmployeeContractHistories.Add(new EmployeeContractHistory
        {
            SchoolId = schoolId,
            EmployeeContractId = contract.Id,
            ChangeType = EmployeeContractChangeType.Closed,
            // Les termes ne changent PAS à la clôture — Previous == New, seule EndDate est nouvelle.
            PreviousBaseSalary = contract.BaseSalary,
            PreviousHourlyRate = contract.HourlyRate,
            PreviousTransportAllowance = contract.TransportAllowance,
            NewBaseSalary = contract.BaseSalary,
            NewHourlyRate = contract.HourlyRate,
            NewTransportAllowance = contract.TransportAllowance,
            EndDate = request.EndDate,
            Reason = request.Reason,
            ChangedByUserId = actorId,
            ChangedAt = timeProvider.GetUtcNow()
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        return new CloseEmployeeContractResult(contract.Id, request.EndDate);
    }
}
