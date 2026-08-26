using SamaEcole.Application.Inventory.Commands.CancelItemAssignment;
using SamaEcole.Application.Inventory.Commands.CreateInventoryCategory;
using SamaEcole.Application.Inventory.Commands.CreateInventoryItem;
using SamaEcole.Application.Inventory.Commands.CreateItemAssignment;
using SamaEcole.Application.Inventory.Commands.DeleteInventoryCategory;
using SamaEcole.Application.Inventory.Commands.DeleteInventoryItem;
using SamaEcole.Application.Inventory.Commands.RecordStockMovement;
using SamaEcole.Application.Inventory.Commands.ReturnItemAssignment;
using SamaEcole.Application.Inventory.Commands.UpdateInventoryCategory;
using SamaEcole.Application.Inventory.Commands.UpdateInventoryItem;
using SamaEcole.Application.Inventory.Queries.GetDischargeNotePdf;
using SamaEcole.Application.Inventory.Queries.GetInventoryCategories;
using SamaEcole.Application.Inventory.Queries.GetInventoryItemDetail;
using SamaEcole.Application.Inventory.Queries.GetInventoryItems;
using SamaEcole.Application.Inventory.Queries.GetInventoryReportPdf;
using SamaEcole.Application.Inventory.Queries.GetItemAssignments;
using SamaEcole.Application.Inventory.Queries.GetStockMovements;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamaEcole.Web.Controllers;

/// <summary>
/// Module Inventaire — /inventory. Contrôleur mince : aucune logique métier ici (AGENTS.md règle #8).
/// L'école n'est jamais un paramètre de requête : elle vient du JWT (règle #10).
///
/// Matrice de droits (docs/Volume_7_Security.md §21) :
///   * LECTURE — tout utilisateur authentifié. Un enseignant doit pouvoir vérifier ce qui lui a été
///     confié et ce que sa classe détient, sans passer par le secrétariat.
///   * CATALOGUE (créer/corriger/archiver une catégorie ou un bien) — Directeur et Secrétariat, même
///     matrice que Classrooms et Infrastructures : c'est de l'administration du patrimoine.
///   * MOUVEMENTS et PRÊTS — Directeur, Secrétariat et SURVEILLANT. Le surveillant est ajouté ici et
///     nulle part ailleurs dans ce module : c'est lui qui distribue les manuels à la rentrée et les
///     récupère en fin d'année. Lui refuser ce droit reviendrait à faire saisir par le secrétariat des
///     remises qu'il n'a pas faites.
///
/// Aucun contrôle de formule d'abonnement : le module est accessible à TOUTES les formules
/// (arbitrage validé — la décision est commerciale, pas technique, voir la remarque de Feature).
/// </summary>
[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public class InventoryController(ISender mediator) : ControllerBase
{
    /// <summary>Administration du catalogue : ce qui EXISTE au patrimoine.</summary>
    private const string CatalogRoles = "Directeur,Secretariat";

    /// <summary>Vie du stock : ce qui BOUGE. Le surveillant en fait partie — voir la remarque de classe.</summary>
    private const string StockRoles = "Directeur,Secretariat,Surveillant";

    public record UpdateInventoryCategoryRequest(string Name, string? Description, uint RowVersion);

    public record UpdateInventoryItemRequest(
        string Name,
        string? Code,
        Guid CategoryId,
        ItemCondition Condition,
        Guid? RoomId,
        string? LocationLabel,
        decimal? UnitPrice,
        bool IsConsumable,
        string? Notes,
        uint RowVersion);

    public record ReturnItemAssignmentRequest(
        int ReturnedQuantity,
        ItemCondition ReturnCondition,
        DateOnly? ReturnedOn,
        bool DeclareRemainderLost,
        uint RowVersion);

    // ------------------------------------------------------------------ Catégories

    [HttpGet("categories")]
    [ProducesResponseType<IReadOnlyList<InventoryCategoryDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListCategories(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInventoryCategoriesQuery(), cancellationToken));

    [HttpPost("categories")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType<InventoryCategoryResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateCategory(
        [FromBody] CreateInventoryCategoryCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListCategories), new { id = result.Id }, result);
    }

    [HttpPut("categories/{id:guid}")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType<InventoryCategoryResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateCategory(
        Guid id, [FromBody] UpdateInventoryCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateInventoryCategoryCommand(id, request.Name, request.Description, request.RowVersion),
            cancellationToken));

    /// <summary>Archive (soft delete) une catégorie. Refusée en 409 si des biens y sont encore rattachés.</summary>
    [HttpDelete("categories/{id:guid}")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteInventoryCategoryCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Catalogue des biens

    [HttpGet("items")]
    [ProducesResponseType<PaginatedInventoryItems>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListItems(
        [FromQuery] GetInventoryItemsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpGet("items/{id:guid}")]
    [ProducesResponseType<InventoryItemDetailDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetItem(Guid id, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetInventoryItemDetailQuery(id), cancellationToken));

    [HttpPost("items")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType<InventoryItemResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateItem(
        [FromBody] CreateInventoryItemCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(GetItem), new { id = result.Id }, result);
    }

    /// <summary>
    /// Corrige la FICHE d'un bien. Aucune quantité n'est modifiable ici : elles ne varient que par un
    /// mouvement journalisé (voir UpdateInventoryItemCommand).
    /// </summary>
    [HttpPut("items/{id:guid}")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType<InventoryItemResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> UpdateItem(
        Guid id, [FromBody] UpdateInventoryItemRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new UpdateInventoryItemCommand
            {
                Id = id,
                Name = request.Name,
                Code = request.Code,
                CategoryId = request.CategoryId,
                Condition = request.Condition,
                RoomId = request.RoomId,
                LocationLabel = request.LocationLabel,
                UnitPrice = request.UnitPrice,
                IsConsumable = request.IsConsumable,
                Notes = request.Notes,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    /// <summary>Archive (soft delete) un bien. Refusé en 409 si des prêts sont encore en cours.</summary>
    [HttpDelete("items/{id:guid}")]
    [Authorize(Roles = CatalogRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteItem(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteInventoryItemCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    // ------------------------------------------------------------------ Journal de stock

    [HttpGet("movements")]
    [ProducesResponseType<PaginatedStockMovements>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListMovements(
        [FromQuery] GetStockMovementsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    /// <summary>
    /// Enregistre une entrée, une sortie, un ajustement d'inventaire ou une mise au rebut. Il n'existe
    /// AUCUN endpoint de modification ni de suppression d'un mouvement : le journal est append-only,
    /// une erreur se corrige par un mouvement inverse (voir StockMovement).
    /// </summary>
    [HttpPost("movements")]
    [Authorize(Roles = StockRoles)]
    [ProducesResponseType<StockMovementResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> RecordMovement(
        [FromBody] RecordStockMovementCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListMovements), new { id = result.Id }, result);
    }

    // ------------------------------------------------------------------ Prêts et décharges

    [HttpGet("assignments")]
    [ProducesResponseType<PaginatedItemAssignments>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAssignments(
        [FromQuery] GetItemAssignmentsQuery query, CancellationToken cancellationToken)
        => Ok(await mediator.Send(query, cancellationToken));

    [HttpPost("assignments")]
    [Authorize(Roles = StockRoles)]
    [ProducesResponseType<ItemAssignmentResult>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> CreateAssignment(
        [FromBody] CreateItemAssignmentCommand command, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(command, cancellationToken);

        return CreatedAtAction(nameof(ListAssignments), new { id = result.Id }, result);
    }

    /// <summary>
    /// Enregistre une restitution, totale ou partielle. Appelable plusieurs fois sur la même fiche
    /// tant qu'elle n'est pas clôturée (voir ReturnItemAssignmentCommand).
    /// </summary>
    [HttpPost("assignments/{id:guid}/return")]
    [Authorize(Roles = StockRoles)]
    [ProducesResponseType<ItemReturnResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ReturnAssignment(
        Guid id, [FromBody] ReturnItemAssignmentRequest request, CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ReturnItemAssignmentCommand
            {
                Id = id,
                ReturnedQuantity = request.ReturnedQuantity,
                ReturnCondition = request.ReturnCondition,
                ReturnedOn = request.ReturnedOn,
                DeclareRemainderLost = request.DeclareRemainderLost,
                RowVersion = request.RowVersion
            },
            cancellationToken));

    /// <summary>
    /// Annule une fiche saisie par erreur et rend les unités au disponible. Refusée en 409 dès qu'une
    /// restitution a été enregistrée : une fiche qui raconte un fait réel se clôture, elle ne s'annule pas.
    /// </summary>
    [HttpDelete("assignments/{id:guid}")]
    [Authorize(Roles = StockRoles)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CancelAssignment(
        Guid id, [FromQuery] uint rowVersion, CancellationToken cancellationToken)
    {
        await mediator.Send(new CancelItemAssignmentCommand(id, rowVersion), cancellationToken);
        return NoContent();
    }

    [HttpGet("assignments/{id:guid}/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDischargeNotePdf(Guid id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDischargeNotePdfQuery(id), cancellationToken);

        // « inline » : la décharge s'imprime directement depuis le navigateur au moment de la remise,
        // elle n'a pas vocation à être téléchargée puis retrouvée dans un dossier (même choix que
        // les billets d'entrée/sortie).
        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"Decharge_{result.Reference}.pdf\"";

        return File(result.Content, "application/pdf");
    }

    // ------------------------------------------------------------------ Fiche d'inventaire global

    [HttpGet("reports/global/pdf")]
    [Authorize(Roles = CatalogRoles)]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetInventoryReportPdf(
        [FromQuery] Guid? categoryId, [FromQuery] Guid? roomId, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetInventoryReportPdfQuery { CategoryId = categoryId, RoomId = roomId },
            cancellationToken);

        Response.Headers["Content-Disposition"] =
            $"inline; filename=\"Inventaire_{DateTime.UtcNow:yyyy-MM-dd}.pdf\"";

        return File(result.Content, "application/pdf");
    }
}
