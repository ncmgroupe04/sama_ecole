using SamaEcole.Application.Common;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Commands.SetStudentPhoto;

public class SetStudentPhotoCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<SetStudentPhotoCommand, SetStudentPhotoResult>
{
    public async Task<SetStudentPhotoResult> Handle(SetStudentPhotoCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante : viser un élève d'une
        // autre école renvoie 404, jamais une modification silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        // Verrou optimiste (AGENTS.md règle #5), même contrat qu'UpdateStudentCommandHandler.
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        student.PhotoData = request.PhotoDataBase64 is null ? null : Convert.FromBase64String(request.PhotoDataBase64);

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == student.Id)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);

        return new SetStudentPhotoResult(
            student.Id, newRowVersion, PhotoDisplay.ToDisplayUrl(student.PhotoData, student.PhotoUrl));
    }
}
