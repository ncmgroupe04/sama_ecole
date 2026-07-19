using FluentValidation;

namespace SamaEcole.Application.Classrooms.Commands.DeleteClassroom;

public class DeleteClassroomCommandValidator : AbstractValidator<DeleteClassroomCommand>
{
    public DeleteClassroomCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
    }
}
