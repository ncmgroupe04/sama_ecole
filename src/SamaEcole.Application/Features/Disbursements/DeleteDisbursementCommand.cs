using MediatR;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Features.Disbursements;

//[Authorize(Roles = "SuperAdmin, Directeur, Finance")]
public record DeleteDisbursementCommand(Guid Id) : IRequest;

public class DeleteDisbursementCommandHandler(
    IApplicationDbContext context,
    ICurrentUserService currentUserService) : IRequestHandler<DeleteDisbursementCommand>
{
    public async Task Handle(DeleteDisbursementCommand request, CancellationToken cancellationToken)
    {
        var disbursement = await context.Disbursements.FindAsync([request.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Disbursement), request.Id);

        disbursement.SoftDelete(currentUserService.UserId?.ToString() ?? "system");
        await context.SaveChangesAsync(cancellationToken);
    }
}
