using MediatR;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Absences.Commands.CancelAbsenceJustification;

/// <summary>Annule un billet d'absence : soft delete, et les lignes d'appel qu'il avait justifiées retrouvent leur statut d'avant.</summary>
public record CancelAbsenceJustificationCommand(Guid Id) : IRequest<int>;

public class CancelAbsenceJustificationCommandHandler(IApplicationDbContext context, ICurrentUserService currentUser)
    : IRequestHandler<CancelAbsenceJustificationCommand, int>
{
    public async Task<int> Handle(CancelAbsenceJustificationCommand request, CancellationToken cancellationToken)
    {
        var justification = await context.AbsenceJustifications.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(AbsenceJustification), request.Id.ToString());

        var restored = await new AbsenceJustificationRegister(context).RestoreAsync(justification.Id, cancellationToken);
        justification.SoftDelete(currentUser.UserId?.ToString() ?? "system");
        await context.SaveChangesAsync(cancellationToken);
        return restored;
    }
}
