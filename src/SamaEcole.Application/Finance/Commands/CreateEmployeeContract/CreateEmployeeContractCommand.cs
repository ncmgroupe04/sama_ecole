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
/// Lié à EXACTEMENT une personne : un enseignant (<see cref="TeacherId"/>) OU un membre du personnel
/// non-enseignant compte utilisateur de la plateforme (<see cref="UserId"/>, ex. Secrétariat,
/// Surveillant, Directeur), jamais les deux ni aucun.
/// </summary>
public record CreateEmployeeContractCommand(
    Guid? TeacherId,
    Guid? UserId,
    ContractType Type,
    decimal BaseSalary,
    decimal HourlyRate,
    decimal TransportAllowance,
    PayoutMethod PayoutMethod = PayoutMethod.Cash,
    string? PayoutAccountReference = null) : IRequest<Guid>;

public class CreateEmployeeContractCommandValidator : AbstractValidator<CreateEmployeeContractCommand>
{
    public CreateEmployeeContractCommandValidator()
    {
        RuleFor(v => v)
            .Must(v => v.TeacherId.HasValue ^ v.UserId.HasValue)
            .WithMessage("Le contrat doit être lié à un enseignant OU un utilisateur, jamais les deux ni aucun.");

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
        else
        {
            var userExists = await context.Users.AnyAsync(u => u.Id == request.UserId!.Value, cancellationToken);
            if (!userExists)
            {
                throw new NotFoundException(nameof(User), request.UserId!.Value);
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
            PayoutMethod = request.PayoutMethod,
            PayoutAccountReference = string.IsNullOrWhiteSpace(request.PayoutAccountReference) ? null : request.PayoutAccountReference.Trim()
        };

        context.EmployeeContracts.Add(contract);
        await context.SaveChangesAsync(cancellationToken);

        return contract.Id;
    }
}
