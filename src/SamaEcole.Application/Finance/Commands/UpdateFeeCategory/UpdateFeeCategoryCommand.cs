using MediatR;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Finance.Commands.UpdateFeeCategory;

/// <summary>
/// PUT /api/v1/finance/fee-categories/{id} — bascule le caractère facultatif d'une catégorie de frais
/// (frais optionnels, Tâche 1). Seul <c>IsOptional</c> est modifiable : le nom et la récurrence restent
/// figés à la création, comme le documente <see cref="DeleteFeeCategory.DeleteFeeCategoryCommand"/>.
///
/// Rendre une catégorie optionnelle (ou obligatoire) n'a d'effet que sur les inscriptions FUTURES : les
/// lignes de frais d'une inscription existante sont un instantané figé (EnrollmentFeeLine), et le montant
/// dû d'une inscription n'est jamais recalculé (AGENTS.md règle #4).
///
/// Audité : décider quels frais les familles peuvent décliner change ce qui leur est facturé.
/// </summary>
public record UpdateFeeCategoryCommand(Guid Id, bool IsOptional) : IRequest<Unit>, IAuditableRequest;
