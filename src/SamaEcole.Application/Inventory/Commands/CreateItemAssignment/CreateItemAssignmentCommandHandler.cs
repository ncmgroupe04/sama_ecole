using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Inventory.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Inventory.Commands.CreateItemAssignment;

public class CreateItemAssignmentCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider)
    : IRequestHandler<CreateItemAssignmentCommand, ItemAssignmentResult>
{
    public async Task<ItemAssignmentResult> Handle(
        CreateItemAssignmentCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var assignedOn = request.AssignedOn ?? today;

        if (assignedOn > today)
        {
            throw Invalid(nameof(request.AssignedOn), "La date de remise ne peut pas être dans le futur.");
        }

        if (request.DueOn is { } dueOn && dueOn < assignedOn)
        {
            throw Invalid(nameof(request.DueOn), "La date de retour prévue ne peut pas précéder la date de remise.");
        }

        var item = await dbContext.InventoryItems
            .FirstOrDefaultAsync(i => i.Id == request.ItemId, cancellationToken)
            ?? throw new KeyNotFoundException($"Bien d'inventaire {request.ItemId} introuvable.");

        // Une craie ou une ramette ne se restitue pas : la sortie d'un consommable s'enregistre comme
        // un mouvement de sortie, pas comme un prêt qu'on attendrait en retour.
        if (item.IsConsumable)
        {
            throw Invalid(
                nameof(request.ItemId),
                $"« {item.Name} » est un consommable : enregistrez une sortie de stock plutôt qu'un prêt.");
        }

        var beneficiaryLabel = await ResolveBeneficiaryLabelAsync(request, cancellationToken);

        var assignment = new ItemAssignment
        {
            SchoolId = schoolId,
            ItemId = item.Id,
            Quantity = request.Quantity,
            BeneficiaryType = request.BeneficiaryType,
            StudentId = request.BeneficiaryType == AssignmentBeneficiaryType.Eleve ? request.BeneficiaryId : null,
            TeacherId = request.BeneficiaryType == AssignmentBeneficiaryType.Enseignant ? request.BeneficiaryId : null,
            UserId = request.BeneficiaryType == AssignmentBeneficiaryType.Personnel ? request.BeneficiaryId : null,
            BeneficiaryLabel = beneficiaryLabel,
            AssignedOn = assignedOn,
            DueOn = request.DueOn,
            Status = AssignmentStatus.EnCours,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim()
        };

        dbContext.ItemAssignments.Add(assignment);

        dbContext.SetOriginalConcurrencyToken(item, request.RowVersion);

        // Refuse en 422 si le disponible ne suffit pas ; deux prêts concurrents du dernier exemplaire
        // se départagent en 409 au SaveChangesAsync (verrou xmin posé juste au-dessus).
        var movement = StockLedger.Apply(
            item,
            StockMovementType.Attribution,
            request.Quantity,
            assignedOn,
            $"Prêt à {beneficiaryLabel}",
            beneficiaryLabel,
            assignment.Id);

        dbContext.StockMovements.Add(movement);

        await dbContext.SaveChangesAsync(cancellationToken);

        var versions = await dbContext.ItemAssignments.AsNoTracking()
            .Where(a => a.Id == assignment.Id)
            .Select(a => new
            {
                RowVersion = EF.Property<uint>(a, "xmin"),
                ItemRowVersion = dbContext.InventoryItems.AsNoTracking()
                    .Where(i => i.Id == a.ItemId)
                    .Select(i => EF.Property<uint>(i, "xmin"))
                    .First()
            })
            .FirstAsync(cancellationToken);

        return new ItemAssignmentResult(
            assignment.Id,
            AssignmentReference.For(assignment.Id, assignment.AssignedOn),
            item.Id,
            item.Name,
            assignment.Quantity,
            assignment.BeneficiaryType.ToString(),
            request.BeneficiaryId,
            assignment.BeneficiaryLabel,
            assignment.AssignedOn,
            assignment.DueOn,
            assignment.Status.ToString(),
            versions.RowVersion,
            versions.ItemRowVersion);
    }

    /// <summary>
    /// Le nom est FIGÉ sur la fiche (voir ItemAssignment.BeneficiaryLabel). La recherche passe par les
    /// DbSet filtrés : un bénéficiaire d'une autre école est structurellement introuvable (Global Query
    /// Filter + RLS), et la requête est refusée en 422 sur le champ précis plutôt qu'en 500 issu de la
    /// contrainte de clé étrangère — même traitement que CreateRoomCommandHandler pour le bâtiment.
    /// </summary>
    private async Task<string> ResolveBeneficiaryLabelAsync(
        CreateItemAssignmentCommand request, CancellationToken cancellationToken)
    {
        var label = request.BeneficiaryType switch
        {
            AssignmentBeneficiaryType.Eleve => await dbContext.Students.AsNoTracking()
                .Where(s => s.Id == request.BeneficiaryId)
                .Select(s => $"{s.FullName} ({s.Matricule})")
                .FirstOrDefaultAsync(cancellationToken),

            AssignmentBeneficiaryType.Enseignant => await dbContext.Teachers.AsNoTracking()
                .Where(t => t.Id == request.BeneficiaryId)
                .Select(t => $"{t.FullName} ({t.Matricule})")
                .FirstOrDefaultAsync(cancellationToken),

            // Les utilisateurs ne portent pas de SchoolId obligatoire (un Super Admin n'appartient à
            // aucune école) : le filtre tenant est donc explicite ici, contrairement aux deux cas
            // ci-dessus où le Global Query Filter s'en charge.
            AssignmentBeneficiaryType.Personnel => await dbContext.Users.AsNoTracking()
                .Where(u => u.Id == request.BeneficiaryId && u.SchoolId == tenantProvider.CurrentSchoolId)
                .Select(u => u.FullName)
                .FirstOrDefaultAsync(cancellationToken),

            _ => throw Invalid(nameof(request.BeneficiaryType), "Type de bénéficiaire inconnu.")
        };

        return label ?? throw Invalid(
            nameof(request.BeneficiaryId),
            "Le bénéficiaire indiqué n'existe pas dans votre établissement.");
    }

    private static ValidationException Invalid(string property, string message) =>
        new([new ValidationFailure(property, message)]);
}
