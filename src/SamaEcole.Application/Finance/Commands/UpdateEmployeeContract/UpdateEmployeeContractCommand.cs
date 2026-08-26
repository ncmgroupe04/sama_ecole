using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.UpdateEmployeeContract;

/// <summary>
/// PATCH /finance/employee-contracts/{id} — Volume 1 §14.1. Change les termes d'un contrat encore
/// ACTIF (salaire de base, taux horaire, prime de transport) : la seule voie prévue pour une
/// augmentation de salaire ou une révision du taux horaire, puisque <see cref="EmployeeContract"/> ne
/// se recrée jamais pour la même personne tant que son contrat courant n'est pas clôturé (index unique
/// TeacherId/UserId, migration AddEmployeeContractLifecycle).
///
/// <see cref="ContractType"/> n'est volontairement PAS modifiable ici : un changement de nature de
/// contrat (Permanent -> Vacataire) est un événement RH assez rare et structurant pour justifier une
/// clôture puis un nouveau contrat, plutôt qu'une bascule silencieuse qui romprait la cohérence de
/// l'historique (un PreviousType différent du NewType aurait fallu tracer, ce que ce ticket ne demande pas).
/// </summary>
public record UpdateEmployeeContractCommand(
    Guid ContractId,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    string Reason,
    uint RowVersion,
    PayoutMethod PayoutMethod = PayoutMethod.Cash,
    string? PayoutAccountReference = null) : IRequest<UpdateEmployeeContractResult>, IAuditableRequest;

public record UpdateEmployeeContractResult(
    Guid ContractId,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    PayoutMethod PayoutMethod,
    string? PayoutAccountReference,
    uint RowVersion);

public class UpdateEmployeeContractCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateEmployeeContractCommand, UpdateEmployeeContractResult>
{
    public async Task<UpdateEmployeeContractResult> Handle(
        UpdateEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser un
        // contrat d'une autre école renvoie 404, jamais une modification silencieuse.
        var contract = await dbContext.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.ContractId, cancellationToken)
            ?? throw new KeyNotFoundException($"Contrat {request.ContractId} introuvable.");

        if (contract.EndDate is not null)
        {
            throw new BusinessRuleException("Ce contrat est clôturé : ses termes ne peuvent plus être modifiés.");
        }

        // Cohérence avec PayrollCalculator (même garde qu'à la création, CreateEmployeeContractCommandValidator) :
        // un Permanent sans salaire de base ou un Vacataire sans taux horaire produirait silencieusement
        // une fiche de paie à 0. Vérifié ICI, pas dans un Validator stateless, puisque Type n'est connu
        // qu'après lecture du contrat existant.
        if (contract.Type == ContractType.Permanent && request.BaseSalary <= 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.BaseSalary), "Un contrat Permanent doit porter un salaire de base.")
            ]);
        }

        if (contract.Type == ContractType.Vacataire && request.HourlyRate <= 0)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.HourlyRate), "Un contrat Vacataire doit porter un taux horaire.")
            ]);
        }

        var normalizedReference = string.IsNullOrWhiteSpace(request.PayoutAccountReference)
            ? null
            : request.PayoutAccountReference.Trim();

        // Rien ne change : ne pas écrire, sans quoi l'historique se remplirait d'entrées vides et le
        // verrou optimiste n'aurait rien à arbitrer.
        if (contract.BaseSalary == request.BaseSalary
            && contract.HourlyRate == request.HourlyRate
            && contract.TransportAllowance == request.TransportAllowance
            && contract.PayoutMethod == request.PayoutMethod
            && contract.PayoutAccountReference == normalizedReference)
        {
            return new UpdateEmployeeContractResult(
                contract.Id, contract.BaseSalary, contract.HourlyRate, contract.TransportAllowance,
                contract.PayoutMethod, contract.PayoutAccountReference, request.RowVersion);
        }

        // Cœur du verrou optimiste : la version LUE PAR LE CLIENT devient la valeur d'origine attendue
        // du jeton xmin (AGENTS.md règle #5). Si le contrat a changé en base depuis l'affichage,
        // SaveChangesAsync lève une ConcurrencyConflictException -> 409.
        dbContext.SetOriginalConcurrencyToken(contract, request.RowVersion);

        // L'entrée EmployeeContractHistory ne trace QUE les montants (AGENTS.md règle #4) : un
        // changement de PayoutMethod seul ne doit jamais produire une ligne Previous==New qui
        // affirmerait un changement de rémunération qui n'a pas eu lieu.
        var amountsChanged = contract.BaseSalary != request.BaseSalary
            || contract.HourlyRate != request.HourlyRate
            || contract.TransportAllowance != request.TransportAllowance;

        if (amountsChanged)
        {
            dbContext.EmployeeContractHistories.Add(new EmployeeContractHistory
            {
                SchoolId = schoolId,
                EmployeeContractId = contract.Id,
                ChangeType = EmployeeContractChangeType.Amended,
                PreviousBaseSalary = contract.BaseSalary,
                PreviousHourlyRate = contract.HourlyRate,
                PreviousTransportAllowance = contract.TransportAllowance,
                NewBaseSalary = request.BaseSalary,
                NewHourlyRate = request.HourlyRate,
                NewTransportAllowance = request.TransportAllowance,
                EndDate = null,
                Reason = request.Reason,
                ChangedByUserId = actorId,
                ChangedAt = timeProvider.GetUtcNow()
            });
        }

        contract.BaseSalary = request.BaseSalary;
        contract.HourlyRate = request.HourlyRate;
        contract.TransportAllowance = request.TransportAllowance;
        contract.PayoutMethod = request.PayoutMethod;
        contract.PayoutAccountReference = normalizedReference;

        await dbContext.SaveChangesAsync(cancellationToken);

        // Nouveau jeton après écriture : PostgreSQL réécrit xmin à tout UPDATE — on le relit pour que
        // le client puisse enchaîner une seconde modification sans recharger toute la grille.
        var newRowVersion = await dbContext.EmployeeContracts
            .AsNoTracking()
            .Where(c => c.Id == contract.Id)
            .Select(c => EF.Property<uint>(c, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateEmployeeContractResult(
            contract.Id, contract.BaseSalary, contract.HourlyRate, contract.TransportAllowance,
            contract.PayoutMethod, contract.PayoutAccountReference, newRowVersion);
    }
}
