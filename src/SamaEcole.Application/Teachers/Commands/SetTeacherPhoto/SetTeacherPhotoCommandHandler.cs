using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Commands.SetTeacherPhoto;

public class SetTeacherPhotoCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<SetTeacherPhotoCommand, SetTeacherPhotoResult>
{
    public async Task<SetTeacherPhotoResult> Handle(SetTeacherPhotoCommand request, CancellationToken cancellationToken)
    {
        var teacher = await dbContext.Teachers
            .FirstOrDefaultAsync(t => t.Id == request.TeacherId, cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.TeacherId} introuvable.");

        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        teacher.PhotoData = request.PhotoDataBase64 is null ? null : Convert.FromBase64String(request.PhotoDataBase64);

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Id == teacher.Id)
            .Select(t => EF.Property<uint>(t, "xmin"))
            .FirstAsync(cancellationToken);

        return new SetTeacherPhotoResult(
            teacher.Id, newRowVersion, PhotoDisplay.ToDisplayUrl(teacher.PhotoData, teacher.PhotoUrl));
    }
}
