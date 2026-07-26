using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Finance.Commands.CreateTeacherHourRecord;

/// <summary>
/// Un jour d'heures effectuées par un contrat Vacataire — alimente la fiche de suivi imprimable
/// (<c>GetHourRecordSheetQuery</c>), pas le calcul de paie lui-même (<c>PayrollCalculator</c> reste
/// alimenté par <c>PayslipDto.HoursWorked</c>, saisi séparément à la génération de la fiche : relier
/// automatiquement les deux est hors périmètre de ce ticket).
/// </summary>
public record CreateTeacherHourRecordCommand(Guid EmployeeContractId, DateOnly Date, decimal Hours, string? Note)
    : IRequest<Guid>;

public class CreateTeacherHourRecordCommandValidator : AbstractValidator<CreateTeacherHourRecordCommand>
{
    public CreateTeacherHourRecordCommandValidator()
    {
        RuleFor(v => v.EmployeeContractId).NotEmpty();
        RuleFor(v => v.Date).NotEmpty();
        RuleFor(v => v.Hours).GreaterThan(0).LessThanOrEqualTo(24);
        RuleFor(v => v.Note).MaximumLength(500);
    }
}

public class CreateTeacherHourRecordCommandHandler(IApplicationDbContext context, ITenantProvider tenantProvider)
    : IRequestHandler<CreateTeacherHourRecordCommand, Guid>
{
    public async Task<Guid> Handle(CreateTeacherHourRecordCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var contract = await context.EmployeeContracts
            .FirstOrDefaultAsync(c => c.Id == request.EmployeeContractId, cancellationToken)
            ?? throw new NotFoundException(nameof(EmployeeContract), request.EmployeeContractId.ToString());

        if (contract.Type != ContractType.Vacataire)
        {
            throw new BusinessRuleException(
                "Le suivi horaire ne concerne que les contrats Vacataire — ce contrat est Permanent.");
        }

        var record = new TeacherHourRecord
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EmployeeContractId = request.EmployeeContractId,
            Date = request.Date,
            Hours = request.Hours,
            Note = request.Note
        };

        context.TeacherHourRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        return record.Id;
    }
}
