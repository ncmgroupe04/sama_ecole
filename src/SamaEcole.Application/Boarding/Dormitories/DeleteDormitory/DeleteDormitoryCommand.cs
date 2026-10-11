using FluentValidation;
using MediatR;

namespace SamaEcole.Application.Boarding.Dormitories.DeleteDormitory;

/// <summary>
/// DELETE /api/v1/boarding/dormitories/{id}?rowVersion= — soft delete (règle #6). Refusé en 409 RESOURCE_IN_USE
/// tant que le pavillon possède des chambres vivantes (même règle que DeleteBuilding).
/// </summary>
public record DeleteDormitoryCommand(Guid Id, uint RowVersion) : IRequest<Unit>;

public class DeleteDormitoryCommandValidator : AbstractValidator<DeleteDormitoryCommand>
{
    public DeleteDormitoryCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}
