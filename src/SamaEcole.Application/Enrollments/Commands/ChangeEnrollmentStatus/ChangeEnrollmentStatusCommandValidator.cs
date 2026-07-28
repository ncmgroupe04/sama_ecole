using SamaEcole.Domain.Enums;
using FluentValidation;

namespace SamaEcole.Application.Enrollments.Commands.ChangeEnrollmentStatus;

public class ChangeEnrollmentStatusCommandValidator : AbstractValidator<ChangeEnrollmentStatusCommand>
{
    public ChangeEnrollmentStatusCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();

        // Seuls l'abandon et le transfert passent par cette commande — Confirmed vient de
        // CreateEnrollmentCommand, Cancelled de CancelEnrollmentCommand, chacun avec sa propre règle.
        RuleFor(x => x.NewStatus)
            .Must(status => status is EnrollmentStatus.DroppedOut or EnrollmentStatus.Transferred)
            .WithMessage("Seuls les statuts 'DroppedOut' (abandon) et 'Transferred' (transfert) sont autorisés via cette commande.");
    }
}
