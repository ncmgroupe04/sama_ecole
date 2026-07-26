using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

public record CreateFinancialCommitmentCommand(Guid EnrollmentId, decimal Amount, DateOnly DueDate, string Terms)
    : IRequest<Guid>;
