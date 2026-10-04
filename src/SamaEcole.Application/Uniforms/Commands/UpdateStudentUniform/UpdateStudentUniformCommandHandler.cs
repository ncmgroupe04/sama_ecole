using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Uniforms.Commands.UpdateStudentUniform;

public class UpdateStudentUniformCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateStudentUniformCommand, StudentUniformDto>
{
    public async Task<StudentUniformDto> Handle(UpdateStudentUniformCommand request, CancellationToken cancellationToken)
    {
        var line = await dbContext.StudentUniforms.FirstOrDefaultAsync(u => u.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Dotation {request.Id} introuvable.");

        if (!UniformDeliveryPolicy.CanEdit(line.Status))
        {
            throw new BusinessRuleException(
                "Cette tenue n'est plus à remettre : sa ligne ne peut plus être modifiée.", "UNIFORM_NOT_EDITABLE");
        }

        string? sizeLabel = null;

        if (request.UniformVariantId is { } variantId)
        {
            var variant = await dbContext.UniformVariants.AsNoTracking()
                .FirstOrDefaultAsync(v => v.Id == variantId && v.UniformItemId == line.UniformItemId, cancellationToken);

            if (variant is null)
            {
                throw new ValidationException([new ValidationFailure(
                    nameof(request.UniformVariantId), "La taille indiquée n'appartient pas au modèle de tenue de cette ligne.")]);
            }

            sizeLabel = variant.SizeLabel;
        }

        dbContext.SetOriginalConcurrencyToken(line, request.RowVersion);

        line.UniformVariantId = request.UniformVariantId;
        line.SizeLabel = sizeLabel;
        line.Quantity = request.Quantity;

        await dbContext.SaveChangesAsync(cancellationToken);

        return (await StudentUniformReader.GetByIdsAsync(dbContext, [line.Id], cancellationToken)).Single();
    }
}
