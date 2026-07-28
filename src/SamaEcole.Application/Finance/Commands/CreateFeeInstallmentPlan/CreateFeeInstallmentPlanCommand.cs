using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFeeInstallmentPlan;

/// <summary>
/// POST /finance/enrollments/{enrollmentId}/installment-plan — échéancier personnalisé (Étape 5,
/// recouvrement). Remplace tout plan déjà ACTIF sur l'inscription (bascule à Cancelled, jamais
/// supprimé — règle #6) : un nouvel accord négocié avec la famille est une renégociation, pas une
/// correction d'un plan mal saisi.
/// </summary>
public record CreateFeeInstallmentPlanCommand(
    Guid EnrollmentId,
    string? Reason,
    IReadOnlyList<CreateFeeInstallmentLine> Installments) : IRequest<Guid>;

public record CreateFeeInstallmentLine(string Label, decimal Amount, DateOnly DueDate);
