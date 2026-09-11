using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Teachers.Commands.CorrectTeacherMatricule;

public class CorrectTeacherMatriculeCommandHandler(
    IApplicationDbContext dbContext,
    ILogger<CorrectTeacherMatriculeCommandHandler> logger)
    : IRequestHandler<CorrectTeacherMatriculeCommand, CorrectTeacherMatriculeResult>
{
    public async Task<CorrectTeacherMatriculeResult> Handle(
        CorrectTeacherMatriculeCommand request,
        CancellationToken cancellationToken)
    {
        var teacher = await dbContext.Teachers
            .FirstOrDefaultAsync(t => t.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Enseignant {request.Id} introuvable.");

        var newMatricule = request.NewMatricule.Trim();

        if (string.Equals(newMatricule, teacher.Matricule, StringComparison.Ordinal))
        {
            var currentVersion = await CurrentRowVersionAsync(teacher.Id, cancellationToken);
            return new CorrectTeacherMatriculeResult(teacher.Id, teacher.Matricule, currentVersion);
        }

        var alreadyUsed = await dbContext.Teachers
            .AnyAsync(t => t.Id != teacher.Id && t.Matricule == newMatricule, cancellationToken);

        if (alreadyUsed)
        {
            throw new DuplicateRecordException(
                $"Le matricule « {newMatricule} » est déjà porté par un autre enseignant de l'établissement. "
                + "Choisissez-en un autre.",
                $"Teacher.Matricule ({newMatricule})");
        }

        dbContext.SetOriginalConcurrencyToken(teacher, request.RowVersion);

        var previous = teacher.Matricule;
        teacher.Matricule = newMatricule;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Matricule de l'enseignant {TeacherId} corrigé de {Previous} en {NewMatricule}.",
            teacher.Id, previous, newMatricule);

        var newVersion = await CurrentRowVersionAsync(teacher.Id, cancellationToken);
        return new CorrectTeacherMatriculeResult(teacher.Id, teacher.Matricule, newVersion);
    }

    private async Task<uint> CurrentRowVersionAsync(Guid teacherId, CancellationToken cancellationToken) =>
        await dbContext.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => EF.Property<uint>(t, "xmin"))
            .FirstAsync(cancellationToken);
}
