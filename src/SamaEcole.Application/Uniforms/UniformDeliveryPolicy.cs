using FluentValidation.Results;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Uniforms;

/// <summary>
/// Module Tenues — la machine à états d'une dotation et les garde-fous de la remise. Pure et sans base : les
/// Handlers l'appliquent AVANT de toucher au stock.
///
/// Deux familles de refus, distinctes comme dans le reste du projet :
///   • l'ÉTAT de la dotation interdit l'opération → <see cref="BusinessRuleException"/> (409) ;
///   • la DONNÉE est incomplète (taille ou motif absent) → <see cref="ValidationException"/> (422).
///
/// <c>Requested → Delivered → Returned</c> ou <c>Requested → Cancelled</c>. Une tenue remise ne s'annule jamais :
/// elle se retourne, et le retour écrit un mouvement de stock inverse (le journal est append-only).
/// </summary>
public static class UniformDeliveryPolicy
{
    /// <summary>Même borne que la colonne <c>NotBilledReason</c> et que le motif de retour.</summary>
    public const int MaxReasonLength = 200;

    /// <summary>Statuts qui occupent la place « une ligne active par élève et par modèle » de l'index unique partiel.</summary>
    public static bool IsActive(StudentUniformStatus status) =>
        status is StudentUniformStatus.Requested or StudentUniformStatus.Delivered;

    /// <summary>Seule une ligne encore demandée se modifie (taille, quantité) : une ligne remise est un fait.</summary>
    public static bool CanEdit(StudentUniformStatus status) => status == StudentUniformStatus.Requested;

    /// <summary>
    /// Refuse (409) toute ligne qui n'est pas « demandée ». Appelée AVANT de lire quoi que ce soit d'autre : une tenue
    /// déjà remise ne se remet pas, sinon le stock sortirait deux fois.
    /// </summary>
    public static void EnsureDeliverable(StudentUniformStatus status)
    {
        if (status != StudentUniformStatus.Requested)
        {
            throw new BusinessRuleException(
                "Cette tenue n'est plus à remettre : elle a déjà été remise, retournée ou annulée.",
                "UNIFORM_NOT_DELIVERABLE");
        }
    }

    /// <summary>
    /// Valide une remise et retourne le motif NORMALISÉ à enregistrer : <c>null</c> pour une dotation facturée (un
    /// motif envoyé quand même est écarté — il n'a de sens que pour une remise non facturée), sinon le motif nettoyé.
    /// </summary>
    public static string? ValidateDelivery(
        StudentUniformStatus status,
        Guid? variantId,
        int quantity,
        UniformBillingStatus billing,
        string? notBilledReason)
    {
        EnsureDeliverable(status);

        var failures = new List<ValidationFailure>();

        if (variantId is null)
        {
            failures.Add(new ValidationFailure("uniformVariantId", "La taille doit être confirmée avant la remise."));
        }

        if (quantity <= 0)
        {
            failures.Add(new ValidationFailure("quantity", "La quantité doit être strictement positive."));
        }

        string? reason = null;
        if (billing == UniformBillingStatus.NotBilled)
        {
            reason = NormalizeReason(notBilledReason);
            if (reason is null)
            {
                failures.Add(new ValidationFailure(
                    "notBilledReason", "Un motif est obligatoire pour remettre une tenue qui n'est pas facturée."));
            }
            else if (reason.Length > MaxReasonLength)
            {
                failures.Add(TooLong("notBilledReason"));
            }
        }

        if (failures.Count > 0) throw new ValidationException(failures);

        return reason;
    }

    /// <summary>Valide un retour (échange, mauvaise taille…) et retourne le motif normalisé, obligatoire.</summary>
    public static string ValidateReturn(StudentUniformStatus status, string? reason)
    {
        if (status != StudentUniformStatus.Delivered)
        {
            throw new BusinessRuleException(
                "Seule une tenue remise peut être retournée.",
                "UNIFORM_NOT_RETURNABLE");
        }

        var normalized = NormalizeReason(reason);

        if (normalized is null)
        {
            throw new ValidationException([new ValidationFailure("reason", "Un motif est obligatoire pour enregistrer un retour.")]);
        }

        if (normalized.Length > MaxReasonLength)
        {
            throw new ValidationException([TooLong("reason")]);
        }

        return normalized;
    }

    /// <summary>Seule une ligne encore demandée s'annule ; une tenue remise se retourne (mouvement inverse).</summary>
    public static void ValidateCancel(StudentUniformStatus status)
    {
        if (status != StudentUniformStatus.Requested)
        {
            throw new BusinessRuleException(
                "Seule une tenue encore à remettre peut être annulée ; une tenue remise se retourne.",
                "UNIFORM_NOT_CANCELLABLE");
        }
    }

    private static string? NormalizeReason(string? reason) =>
        string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static ValidationFailure TooLong(string property) =>
        new(property, $"Le motif ne doit pas dépasser {MaxReasonLength} caractères.");
}
