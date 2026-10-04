using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Uniforms.Commands.CreateUniformItem;

public class CreateUniformItemCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateUniformItemCommand, UniformItemDto>
{
    public async Task<UniformItemDto> Handle(CreateUniformItemCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le frais doit exister DANS CETTE ÉCOLE : le Global Query Filter rend celui d'une autre école introuvable.
        if (request.FeeCategoryId is { } feeCategoryId
            && !await dbContext.FeeCategories.AnyAsync(c => c.Id == feeCategoryId, cancellationToken))
        {
            throw new ValidationException([new ValidationFailure(
                nameof(request.FeeCategoryId), "Le type de frais indiqué n'existe pas dans votre établissement.")]);
        }

        var item = new UniformItem
        {
            SchoolId = schoolId,
            Name = request.Name.Trim(),
            Kind = request.Kind,
            Gender = request.Gender,
            Cycle = request.Cycle,
            FeeCategoryId = request.FeeCategoryId,
            QuantityIncluded = request.QuantityIncluded,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        dbContext.UniformItems.Add(item);

        // Un nom déjà pris pour ce public viole l'index unique partiel : SaveChangesAsync le traduit en
        // DuplicateRecordException -> 409, jamais un 500 ni un doublon silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        return await UniformCatalogReader.GetItemAsync(dbContext, item.Id, cancellationToken);
    }
}
