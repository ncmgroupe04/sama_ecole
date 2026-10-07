using FluentValidation;
using MediatR;
using SamaEcole.Application.Common.Validation;

namespace SamaEcole.Application.Boarding.Rooms.CreateDormitoryRoom;

/// <summary>
/// POST /api/v1/boarding/rooms — crée une chambre ET ses lits 1..<see cref="BedCount"/> en une seule transaction
/// (tout ou rien). La capacité n'est jamais saisie : elle se dérive du nombre de lits (spec N1).
/// <see cref="DormitoryId"/> doit désigner un pavillon DE CETTE ÉCOLE — vérifié par le Handler.
/// </summary>
public record CreateDormitoryRoomCommand : IRequest<DormitoryRoomResult>
{
    public Guid DormitoryId { get; init; }
    public required string Name { get; init; }
    public int BedCount { get; init; }
}

public class CreateDormitoryRoomCommandValidator : AbstractValidator<CreateDormitoryRoomCommand>
{
    public CreateDormitoryRoomCommandValidator()
    {
        RuleFor(x => x.DormitoryId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100).NoHtml();
        RuleFor(x => x.BedCount).InclusiveBetween(1, 40).WithMessage("Une chambre compte entre 1 et 40 lits.");
    }
}
