using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.CreateEmployeeContract;

/// <summary>
/// Contrat de travail — prérequis à la génération d'une fiche de paie (<c>GenerateFichePaieCommand</c>).
/// Lié à EXACTEMENT une personne : un enseignant (<see cref="TeacherId"/>), OU un membre du personnel
/// non-enseignant compte utilisateur de la plateforme (<see cref="UserId"/>, ex. Secrétariat,
/// Surveillant, Directeur), OU un membre du personnel SANS compte (<see cref="ExternalFullName"/> : gardien,
/// cuisinier, agent d'entretien… Volume 1 §14.7), jamais deux à la fois ni aucun.
/// </summary>
public record CreateEmployeeContractCommand(
    Guid? TeacherId,
    Guid? UserId,
    ContractType Type,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    PayoutMethod PayoutMethod = PayoutMethod.Cash,
    string? PayoutAccountReference = null,
    string? ExternalFullName = null,
    string? ExternalJobTitle = null,
    string? ExternalPhone = null,
    bool ApplyStatutoryDeductions = true) : IRequest<Guid>;

public class CreateEmployeeContractCommandValidator : AbstractValidator<CreateEmployeeContractCommand>
{
    public CreateEmployeeContractCommandValidator()
    {
        RuleFor(v => v)
            .Must(v => new[] { v.TeacherId.HasValue, v.UserId.HasValue, !string.IsNullOrWhiteSpace(v.ExternalFullName) }.Count(x => x) == 1)
            .WithMessage("Le contrat doit être lié à un enseignant, à un utilisateur OU à une personne sans compte (nom saisi), jamais plusieurs ni aucun.");
        RuleFor(v => v.ExternalFullName).MaximumLength(150);
        RuleFor(v => v.ExternalJobTitle).MaximumLength(100);
        RuleFor(v => v.ExternalPhone).MaximumLength(30);

        RuleFor(v => v.Type).IsInEnum();
        RuleFor(v => v.BaseSalary).GreaterThanOrEqualTo(0);
        RuleFor(v => v.HourlyRate).GreaterThanOrEqualTo(0);
        RuleFor(v => v.TransportAllowance).GreaterThanOrEqualTo(0);
        RuleFor(v => v.PayoutMethod).IsInEnum();
        RuleFor(v => v.PayoutAccountReference).MaximumLength(50);

        // Cohérence avec PayrollCalculator : un salaire se calcule sur HourlyRate s'il est renseigné,
        // sinon sur BaseSalary — un contrat Permanent sans BaseSalary ou Vacataire sans HourlyRate
        // produirait donc silencieusement une fiche de paie à 0.
        RuleFor(v => v.BaseSalary)
            .GreaterThan(0)
            .When(v => v.Type == ContractType.Permanent)
            .WithMessage("Un contrat Permanent doit porter un salaire de base.");
        RuleFor(v => v.HourlyRate)
            .GreaterThan(0)
            .When(v => v.Type == ContractType.Vacataire)
            .WithMessage("Un contrat Vacataire doit porter un taux horaire.");
    }
}

public class CreateEmployeeContractCommandHandler(IApplicationDbContext context, ITenantProvider tenantProvider)
    : IRequestHandler<CreateEmployeeContractCommand, Guid>
{
    public async Task<Guid> Handle(CreateEmployeeContractCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException();

        if (request.TeacherId.HasValue)
        {
            var teacherExists = await context.Teachers.AnyAsync(t => t.Id == request.TeacherId.Value, cancellationToken);
            if (!teacherExists)
            {
                throw new NotFoundException(nameof(Teacher), request.TeacherId.Value);
            }
        }
        else if (request.UserId.HasValue)
        {
            var userExists = await context.Users.AnyAsync(u => u.Id == request.UserId!.Value, cancellationToken);
            if (!userExists)
            {
                throw new NotFoundException(nameof(User), request.UserId!.Value);
            }
        }

        else
        {
            // Personnel sans compte : pas de clé d'unicité naturelle (contrairement à TeacherId/UserId), donc un
            // garde-fou sur le nom, sans quoi un double clic ou un doublon de saisie créerait deux contrats actifs.
            var name = request.ExternalFullName!.Trim();
            var duplicate = await context.EmployeeContracts.AnyAsync(
                c => c.TeacherId == null && c.UserId == null && c.EndDate == null
                     && c.ExternalFullName != null && c.ExternalFullName.ToLower() == name.ToLower(),
                cancellationToken);
            if (duplicate)
            {
                throw new BusinessRuleException($"Un contrat actif existe déjà pour « {name} ».");
            }
        }

        var contract = new EmployeeContract
        {
            SchoolId = schoolId,
            TeacherId = request.TeacherId,
            UserId = request.UserId,
            Type = request.Type,
            BaseSalary = request.BaseSalary,
            HourlyRate = request.HourlyRate,
            TransportAllowance = request.TransportAllowance,
            ApplyStatutoryDeductions = request.ApplyStatutoryDeductions,
            PayoutMethod = request.PayoutMethod,
            PayoutAccountReference = string.IsNullOrWhiteSpace(request.PayoutAccountReference) ? null : request.PayoutAccountReference.Trim(),
            ExternalFullName = Clean(request.ExternalFullName),
            ExternalJobTitle = Clean(request.ExternalJobTitle),
            ExternalPhone = Clean(request.ExternalPhone)
        };

        context.EmployeeContracts.Add(contract);
        await context.SaveChangesAsync(cancellationToken);

        return contract.Id;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
