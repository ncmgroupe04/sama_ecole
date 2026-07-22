using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Queries.GetPayments;

public class GetPaymentsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetPaymentsQuery, PaginatedPayments>
{
    public async Task<PaginatedPayments> Handle(GetPaymentsQuery request, CancellationToken cancellationToken)
    {
        var query = from p in dbContext.Payments.AsNoTracking()
                    join e in dbContext.Enrollments.AsNoTracking() on p.EnrollmentId equals e.Id
                    join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
                    join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
                    where p.Status != PaymentStatus.Cancelled
                    select new { Payment = p, Enrollment = e, Student = s, Classroom = c };

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim().ToLower();
            query = query.Where(x =>
                x.Payment.ReceiptNumber.ToLower().Contains(search) ||
                x.Student.Matricule.ToLower().Contains(search) ||
                x.Student.FullName.ToLower().Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(request.Method) &&
            Enum.TryParse<PaymentMethod>(request.Method, true, out var method))
        {
            query = query.Where(x => x.Payment.Method == method);
        }

        if (request.ClassroomId.HasValue && request.ClassroomId.Value != Guid.Empty)
        {
            query = query.Where(x => x.Enrollment.ClassroomId == request.ClassroomId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return new PaginatedPayments([], 0, request.Page, request.PageSize);
        }

        var items = await query
            .OrderByDescending(x => x.Payment.PaidAt)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(x => new PaymentListItem(
                x.Payment.Id,
                x.Payment.Id,
                x.Payment.ReceiptNumber,
                x.Student.Matricule,
                x.Student.FullName,
                x.Classroom.Name,
                x.Payment.Amount,
                x.Payment.Method.ToString(),
                x.Payment.Status.ToString(),
                x.Payment.PaidAt))
            .ToListAsync(cancellationToken);

        return new PaginatedPayments(items, totalCount, request.Page, request.PageSize);
    }
}
