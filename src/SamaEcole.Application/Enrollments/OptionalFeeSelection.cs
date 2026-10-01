namespace SamaEcole.Application.Enrollments;

/// <summary>
/// Frais optionnels — décide quels frais d'une classe entrent dans le dû annuel d'une inscription, en
/// fonction des cases cochées (<c>CreateEnrollmentCommand.OptionalFeeCategoryIds</c>). Pure et sans base :
/// c'est elle que le Handler applique, et elle qui porte les trois invariants de la facture.
///
///   1. Un frais OBLIGATOIRE est toujours facturé, quoi que contienne la liste : le client ne peut pas
///      décocher l'inscription ou la mensualité (AGENTS.md règle #10 — le serveur décide du dû).
///   2. Un frais OPTIONNEL n'est facturé que s'il est dans la liste ; liste vide = aucun.
///   3. Liste ABSENTE (<c>null</c>) = comportement historique : tout est facturé. Un client antérieur à la
///      fonctionnalité ne change pas de facture ; c'est le formulaire qui envoie explicitement les choix.
/// </summary>
public static class OptionalFeeSelection
{
    public static bool IsBilled(bool isOptional, Guid feeCategoryId, IReadOnlyCollection<Guid>? selectedOptionalIds)
        => !isOptional || selectedOptionalIds is null || selectedOptionalIds.Contains(feeCategoryId);

    /// <summary>
    /// Identifiants de la liste qui ne sont PAS un frais optionnel de la classe : catégorie inconnue, d'une
    /// autre école, sans tarif sur cette classe — ou obligatoire. Le Handler les rejette en 422 plutôt que
    /// de les ignorer : « cocher » un frais obligatoire est une erreur ou un contournement, pas un no-op.
    /// </summary>
    public static IReadOnlyList<Guid> FindInvalid(
        IReadOnlyCollection<Guid>? selectedOptionalIds, IReadOnlySet<Guid> optionalCategoryIdsOfClass)
        => selectedOptionalIds is null
            ? []
            : selectedOptionalIds.Where(id => !optionalCategoryIdsOfClass.Contains(id)).Distinct().ToList();
}
