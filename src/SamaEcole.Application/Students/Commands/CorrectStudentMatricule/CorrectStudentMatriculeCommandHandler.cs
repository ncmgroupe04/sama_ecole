using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Students.Commands.CorrectStudentMatricule;

public class CorrectStudentMatriculeCommandHandler(
    IApplicationDbContext dbContext,
    ILogger<CorrectStudentMatriculeCommandHandler> logger)
    : IRequestHandler<CorrectStudentMatriculeCommand, CorrectStudentMatriculeResult>
{
    public async Task<CorrectStudentMatriculeResult> Handle(
        CorrectStudentMatriculeCommand request,
        CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent la recherche à l'école courante : viser un
        // élève d'une autre école renvoie 404, jamais une correction silencieuse.
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.Id} introuvable.");

        var newMatricule = request.NewMatricule.Trim();

        // Report sans changement : on renvoie l'état courant sans écrire — évite un 409 optimiste
        // fantôme si l'écran renvoie la valeur inchangée.
        if (string.Equals(newMatricule, student.Matricule, StringComparison.Ordinal))
        {
            var currentVersion = await CurrentRowVersionAsync(student.Id, cancellationToken);
            return new CorrectStudentMatriculeResult(student.Id, student.Matricule, currentVersion);
        }

        // Pré-contrôle d'unicité (SchoolId, Matricule) : l'index unique en base l'imposerait de toute
        // façon, mais une phrase claire vaut mieux qu'une erreur de contrainte réécrite.
        var alreadyUsed = await dbContext.Students
            .AnyAsync(s => s.Id != student.Id && s.Matricule == newMatricule, cancellationToken);

        if (alreadyUsed)
        {
            throw new DuplicateRecordException(
                $"Le matricule « {newMatricule} » est déjà porté par un autre élève de l'établissement. "
                + "Choisissez-en un autre.",
                $"Student.Matricule ({newMatricule})");
        }

        // Verrou optimiste (AGENTS.md règle #5) : une fiche modifiée en base depuis sa lecture fait
        // échouer SaveChangesAsync en 409, jamais un écrasement silencieux.
        dbContext.SetOriginalConcurrencyToken(student, request.RowVersion);

        var previous = student.Matricule;
        student.Matricule = newMatricule;

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Matricule de l'élève {StudentId} corrigé de {Previous} en {NewMatricule}.",
            student.Id, previous, newMatricule);

        var newVersion = await CurrentRowVersionAsync(student.Id, cancellationToken);
        return new CorrectStudentMatriculeResult(student.Id, student.Matricule, newVersion);
    }

    private async Task<uint> CurrentRowVersionAsync(Guid studentId, CancellationToken cancellationToken) =>
        await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);
}
