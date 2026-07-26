using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using MediatR;

namespace SamaEcole.Application.Finance.Commands.CreateFinancialCommitment;

/// <summary>
/// Enregistre un engagement financier (reconnaissance de dette) — une trace écrite de l'accord, PAS
/// un encaissement : ne touche jamais <see cref="Enrollment.AmountPaid"/> ni <see cref="Enrollment.TotalDue"/>
/// (AGENTS.md règle #4, seul un <c>Payment</c> réel modifie le solde).
/// </summary>
public class CreateFinancialCommitmentCommandHandler(
    IApplicationDbContext _context,
    ITenantProvider _tenantProvider)
    : IRequestHandler<CreateFinancialCommitmentCommand, Guid>
{
    public async Task<Guid> Handle(CreateFinancialCommitmentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = _tenantProvider.CurrentSchoolId ?? throw new UnauthorizedAccessException("Tenant is required.");

        var enrollmentExists = await _context.Enrollments.FindAsync(new object[] { request.EnrollmentId }, cancellationToken);
        if (enrollmentExists == null)
            throw new NotFoundException(nameof(Enrollment), request.EnrollmentId.ToString());

        var commitment = new FinancialCommitment
        {
            Id = Guid.NewGuid(),
            SchoolId = schoolId,
            EnrollmentId = request.EnrollmentId,
            Amount = request.Amount,
            DueDate = request.DueDate,
            Terms = request.Terms
        };

        _context.FinancialCommitments.Add(commitment);
        await _context.SaveChangesAsync(cancellationToken);

        return commitment.Id;
    }
}
