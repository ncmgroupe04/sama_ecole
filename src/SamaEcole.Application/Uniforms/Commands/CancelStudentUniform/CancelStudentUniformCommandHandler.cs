using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms.Commands.CancelStudentUniform;

public class CancelStudentUniformCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<CancelStudentUniformCommand, Unit>
{
    public async Task<Unit> Handle(CancelStudentUniformCommand request, CancellationToken cancellationToken)
    {
        var line = await dbContext.StudentUniforms.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dotation {request.Id} introuvable.");

        UniformDeliveryPolicy.ValidateCancel(line.Status);

        dbContext.SetOriginalConcurrencyToken(line, request.RowVersion);

        line.Status = StudentUniformStatus.Cancelled;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
