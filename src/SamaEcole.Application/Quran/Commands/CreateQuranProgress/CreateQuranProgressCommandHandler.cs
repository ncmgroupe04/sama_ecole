using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Quran.Commands.CreateQuranProgress;

public class CreateQuranProgressCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateQuranProgressCommand, QuranProgressDto>
{
    public async Task<QuranProgressDto> Handle(CreateQuranProgressCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter restreint déjà la recherche à l'école courante : un élève d'une
        // autre école y est structurellement introuvable (même idiome que CreateGradeCommandHandler).
        if (!await dbContext.Students.AnyAsync(s => s.Id == request.StudentId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.StudentId), "L'élève indiqué n'existe pas dans votre établissement.")
            ]);
        }

        var entry = new QuranProgress
        {
            SchoolId = schoolId,
            StudentId = request.StudentId,
            JuzNumber = request.JuzNumber,
            HizbNumber = request.HizbNumber,
            SurahNumber = request.SurahNumber,
            Status = request.Status,
            EvaluationDate = request.EvaluationDate,
            Notes = request.Notes
        };

        dbContext.QuranProgresses.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.QuranProgresses.AsNoTracking()
            .Where(p => p.Id == entry.Id)
            .Select(p => EF.Property<uint>(p, "xmin"))
            .FirstAsync(cancellationToken);

        return new QuranProgressDto(
            entry.Id, entry.StudentId, entry.JuzNumber, entry.HizbNumber, entry.SurahNumber,
            entry.Status, entry.EvaluationDate, entry.Notes, rowVersion);
    }
}
