using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Finance.Commands.ApplyStandardFee;

public class ApplyStandardFeeCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    TimeProvider timeProvider)
    : IRequestHandler<ApplyStandardFeeCommand, ApplyStandardFeeResult>
{
    public async Task<ApplyStandardFeeResult> Handle(ApplyStandardFeeCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur courant inconnu.");

        // La catégorie doit exister DANS CETTE ÉCOLE. Le Global Query Filter borne déjà la requête au
        // tenant : une catégorie d'une autre école est introuvable ici, et le contrôle vaut
        // vérification d'appartenance autant que d'existence.
        var categoryExists = await dbContext.FeeCategories
            .AnyAsync(c => c.Id == request.FeeCategoryId, cancellationToken);

        if (!categoryExists)
        {
            throw new ValidationException([
                new ValidationFailure(
                    nameof(request.FeeCategoryId),
                    "La catégorie de frais indiquée n'existe pas dans votre établissement.")
            ]);
        }

        // Périmètre d'application : tout l'établissement, ou le seul niveau ciblé. La comparaison
        // passe par lower() côté PostgreSQL — le niveau est saisi librement par l'école, « Collège »
        // et « collège » doivent désigner les mêmes classes.
        var level = request.Level?.Trim();
        var normalizedLevel = string.IsNullOrEmpty(level) ? null : level.ToLowerInvariant();

        var classroomsQuery = dbContext.Classrooms.AsQueryable();
        if (normalizedLevel is not null)
        {
            classroomsQuery = classroomsQuery.Where(c => c.Level.ToLower() == normalizedLevel);
        }

        var classroomIds = await classroomsQuery
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (classroomIds.Count == 0)
        {
            // Aucune cible : rien à facturer. On ne crée pas une catégorie « fantôme » sans classe,
            // et un niveau vide est signalé sur SON champ — sinon l'utilisateur corrigerait le montant
            // alors que c'est le périmètre qui ne désigne rien.
            throw new ValidationException([
                normalizedLevel is null
                    ? new ValidationFailure(
                        nameof(request.FeeCategoryId),
                        "Aucune classe n'est encore définie : créez vos classes avant de paramétrer les frais.")
                    : new ValidationFailure(
                        nameof(request.Level),
                        $"Aucune classe n'est rattachée au niveau « {level} ».")
            ]);
        }

        // Lignes déjà présentes pour cette catégorie DANS LE PÉRIMÈTRE, indexées par classe. Le filtre
        // sur classroomIds n'est pas qu'une optimisation : hors périmètre, aucune entité n'est même
        // chargée, donc aucun montant d'un autre niveau ne peut être modifié par mégarde. On suit ces
        // entités (pas d'AsNoTracking) : on va potentiellement modifier leur montant, et EF a besoin
        // de leur jeton xmin pour le verrou optimiste.
        var existingByClassroom = await dbContext.ClassFees
            .Where(f => f.FeeCategoryId == request.FeeCategoryId && classroomIds.Contains(f.ClassroomId))
            .ToDictionaryAsync(f => f.ClassroomId, cancellationToken);

        var now = timeProvider.GetUtcNow();
        int created = 0, updated = 0, skipped = 0;

        // Une seule transaction : soit tout le périmètre bascule sur le standard, soit rien. Un échec
        // au milieu ne doit pas laisser la moitié des classes réalignées et l'autre non.
        await dbContext.ExecuteInTransactionAsync(async ct =>
        {
            foreach (var classroomId in classroomIds)
            {
                if (!existingByClassroom.TryGetValue(classroomId, out var fee))
                {
                    // Classe encore vierge : on crée la ligne de barème et sa première entrée d'audit
                    // (ancien montant NULL = définition initiale).
                    var newFee = new ClassFee
                    {
                        SchoolId = schoolId,
                        FeeCategoryId = request.FeeCategoryId,
                        ClassroomId = classroomId,
                        Amount = request.Amount
                    };
                    dbContext.ClassFees.Add(newFee);
                    AddHistory(schoolId, newFee.Id, oldAmount: null, request.Amount, actorId, now);
                    created++;
                }
                else if (request.OverwriteExisting && fee.Amount != request.Amount)
                {
                    // Réalignement assumé d'une classe déjà définie : historisé (ancien → nouveau).
                    AddHistory(schoolId, fee.Id, fee.Amount, request.Amount, actorId, now);
                    fee.Amount = request.Amount;
                    updated++;
                }
                else
                {
                    // Exception préservée (OverwriteExisting=false), ou montant déjà identique : rien
                    // à écrire, et surtout aucune entrée d'audit vide.
                    skipped++;
                }
            }

            await dbContext.SaveChangesAsync(ct);
            return 0;
        }, cancellationToken);

        return new ApplyStandardFeeResult(created, updated, skipped);
    }

    private void AddHistory(Guid schoolId, Guid classFeeId, decimal? oldAmount, decimal newAmount, Guid actorId, DateTimeOffset now)
        => dbContext.FeeChangeHistory.Add(new FeeChangeHistory
        {
            SchoolId = schoolId,
            ClassFeeId = classFeeId,
            OldAmount = oldAmount,
            NewAmount = newAmount,
            ChangedByUserId = actorId,
            ChangedAt = now
        });
}
