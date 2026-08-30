using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.StateIntegration.Commands.AssignStudentIen;

public class AssignStudentIenCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    IIenGeneratorService ienGenerator)
    : IRequestHandler<AssignStudentIenCommand, AssignStudentIenResult>
{
    public async Task<AssignStudentIenResult> Handle(
        AssignStudentIenCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Le Global Query Filter borne à l'école courante : un élève d'une autre école est
        // structurellement introuvable ici (404, jamais une écriture croisée entre tenants).
        var student = await dbContext.Students
            .FirstOrDefaultAsync(s => s.Id == request.StudentId, cancellationToken)
            ?? throw new KeyNotFoundException("Élève introuvable dans votre établissement.");

        return request.IenNumber is { } submitted
            ? await AssignOfficialAsync(student.Id, submitted, cancellationToken)
            : await AssignProvisionalAsync(student.Id, schoolId, cancellationToken);
    }

    private async Task<AssignStudentIenResult> AssignOfficialAsync(
        Guid studentId, string submitted, CancellationToken cancellationToken)
    {
        var normalized = submitted.Trim().ToUpperInvariant();

        // Contrôle de FORME seulement : personne ici ne peut affirmer que ce numéro existe au fichier
        // national. Le message d'erreur ne doit donc jamais laisser croire à une vérification auprès
        // du ministère — voir IIenGeneratorService.IsWellFormed.
        if (!ienGenerator.IsWellFormed(normalized))
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(AssignStudentIenCommand.IenNumber),
                    "Ce numéro n'a pas une forme d'IEN valide. Vérifiez la saisie — "
                    + "aucun contrôle auprès du SIMEN n'est possible à ce jour.")
            ]);
        }

        // Doublon DANS L'ÉCOLE : deux élèves ne peuvent pas porter le même IEN. L'index unique partiel
        // le refuserait de toute façon, mais sous forme de DbUpdateException remontée en 500 — l'agent
        // de saisie mérite de savoir QUEL élève porte déjà ce numéro.
        var duplicateHolder = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id != studentId && s.IenNumber == normalized)
            .Select(s => new { s.FullName, s.Matricule })
            .FirstOrDefaultAsync(cancellationToken);

        if (duplicateHolder is not null)
        {
            throw new BusinessRuleException(
                $"L'IEN {normalized} est déjà attribué à {duplicateHolder.FullName} "
                + $"({duplicateHolder.Matricule}). Un IEN identifie un seul élève.");
        }

        var student = await dbContext.Students
            .FirstAsync(s => s.Id == studentId, cancellationToken);

        student.IenNumber = normalized;

        // Le numéro officiel écrase le provisoire ET retire le marqueur : à partir d'ici, l'export
        // Planète cesse de signaler la ligne comme fabriquée, ce qui est exactement le but.
        student.IsIenProvisional = false;

        await dbContext.SaveChangesAsync(cancellationToken);

        return new AssignStudentIenResult(studentId, normalized, IsProvisional: false);
    }

    private async Task<AssignStudentIenResult> AssignProvisionalAsync(
        Guid studentId, Guid schoolId, CancellationToken cancellationToken)
    {
        // Génération ET écriture dans UNE transaction (AGENTS.md règle #3) : si l'écriture échoue,
        // le compteur de séquence est rembobiné avec elle et aucun numéro n'est perdu.
        return await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            var student = await dbContext.Students.FirstAsync(s => s.Id == studentId, ct);

            // REFUS d'écraser un IEN OFFICIEL par un provisoire. Le sens de la substitution n'est pas
            // symétrique : remplacer un numéro fabriqué par le vrai est un progrès, l'inverse est une
            // perte de donnée irréversible que rien ne justifie.
            if (student.IenNumber is not null && !student.IsIenProvisional)
            {
                throw new BusinessRuleException(
                    $"L'élève porte déjà l'IEN officiel {student.IenNumber}. "
                    + "Un numéro provisoire ne peut pas remplacer un IEN délivré par le ministère.");
            }

            var provisional = await ienGenerator.GenerateProvisionalIenAsync(schoolId, ct);

            student.IenNumber = provisional;
            student.IsIenProvisional = true;

            await dbContext.SaveChangesAsync(ct);

            return new AssignStudentIenResult(studentId, provisional, IsProvisional: true);
        }, cancellationToken);
    }
}
