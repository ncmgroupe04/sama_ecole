using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.LinkTeacherUserAccount;

public class LinkTeacherUserAccountCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider)
    : IRequestHandler<LinkTeacherUserAccountCommand, LinkTeacherUserAccountResult>
{
    public async Task<LinkTeacherUserAccountResult> Handle(
        LinkTeacherUserAccountCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var teacher = await dbContext.Teachers
            .FirstOrDefaultAsync(t => t.Id == request.TeacherId, cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.TeacherId} introuvable.");

        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        if (request.UserId is { } userId)
        {
            await TeacherUserAccountLink.EnsureLinkableAsync(
                dbContext, schoolId, userId, teacher.Id, cancellationToken);
        }

        teacher.UserId = request.UserId;

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Id == teacher.Id)
            .Select(t => EF.Property<uint>(t, "xmin"))
            .FirstAsync(cancellationToken);

        return new LinkTeacherUserAccountResult(teacher.Id, teacher.UserId, newRowVersion);
    }
}
