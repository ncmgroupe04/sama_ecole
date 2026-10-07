using MediatR;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.SoftDelete;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Boarding.Dormitories.CreateDormitory;

public class CreateDormitoryCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateDormitoryCommand, DormitoryDto>
{
    public async Task<DormitoryDto> Handle(CreateDormitoryCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        await DormitoryReader.EnsureValidSupervisorAsync(dbContext, schoolId, request.SupervisorUserId, cancellationToken);

        var name = request.Name.Trim();
        await SoftDeleteLifecycle.EnsureNoArchivedIdentityAsync(
            dbContext.Dormitories, schoolId, d => d.Name == name, $"Un pavillon « {name} »", cancellationToken);

        var dormitory = new Dormitory
        {
            SchoolId = schoolId,
            Name = name,
            Gender = request.Gender,
            // Compte lié → le nom vient du compte à la lecture : on ne stocke pas une copie qui divergerait.
            SupervisorName = request.SupervisorUserId is null ? request.SupervisorName?.Trim() : null,
            SupervisorPhone = request.SupervisorPhone?.Trim(),
            SupervisorUserId = request.SupervisorUserId,
            Notes = request.Notes?.Trim()
        };

        dbContext.Dormitories.Add(dormitory);

        // Un doublon concurrent viole l'index unique partiel : SaveChangesAsync le traduit en
        // ConcurrencyConflictException → 409, jamais un écrasement silencieux ni un 500 (règle #5).
        await dbContext.SaveChangesAsync(cancellationToken);

        return await DormitoryReader.GetDtoAsync(dbContext, dormitory.Id, cancellationToken);
    }
}
