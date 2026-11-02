using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.Finance.Services;

/// <summary>
/// Annulation d'une fiche de paie (Volume 1 §14.5), partagée par « Annuler » et « Annuler et régénérer ».
/// La fiche n'est ni supprimée ni réécrite : elle est marquée annulée (auteur, date, motif). Si elle avait été
/// payée, le décaissement « Salaires » correspondant est CONTRE-PASSÉ (écriture inverse datée du jour), jamais
/// supprimé — même principe que <c>DeleteDisbursementCommand</c>. L'appelant sauvegarde.
/// </summary>
public static class FichePaieCancellation
{
    public const string PeriodDeclaredCode = "PAYSLIP_PERIOD_DECLARED";

    /// <returns>Vrai si une contre-écriture de trésorerie a été ajoutée (cache KPI à invalider).</returns>
    public static async Task<bool> ApplyAsync(
        IApplicationDbContext context,
        FichePaie fiche,
        string reason,
        Guid actorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (fiche.CancelledAt is not null)
        {
            throw new BusinessRuleException("Cette fiche de paie est déjà annulée.");
        }

        // Une déclaration fiscale déjà générée a figé les totaux IPRES/CSS/BRS de la période : annuler une de ses
        // fiches la rendrait fausse sans que personne ne le voie. La déclaration est un document déposé.
        if (await context.TaxeDeclarations.AnyAsync(t => t.Month == fiche.Month && t.Year == fiche.Year, cancellationToken))
        {
            throw new BusinessRuleException(
                $"Une déclaration fiscale existe déjà pour {fiche.Month:00}/{fiche.Year} : cette fiche ne peut plus être annulée.",
                PeriodDeclaredCode);
        }

        var reversed = false;
        if (fiche.DisbursementId is Guid disbursementId)
        {
            var original = await context.Disbursements.AsNoTracking()
                .FirstOrDefaultAsync(d => d.Id == disbursementId, cancellationToken);

            // Si la Finance a déjà annulé ce décaissement à la main depuis l'écran des dépenses, ne pas le
            // contre-passer une seconde fois (la trésorerie serait faussée dans l'autre sens).
            if (original is not null
                && original.ReversalOfId is null
                && !await context.Disbursements.AnyAsync(d => d.ReversalOfId == original.Id, cancellationToken))
            {
                const string prefix = "Annulation — ";
                var label = prefix + original.Reason;
                if (label.Length > 255) label = label[..255];

                context.Disbursements.Add(new Disbursement
                {
                    SchoolId = original.SchoolId,
                    Reason = label,
                    Category = original.Category,
                    Amount = -original.Amount,
                    VatRate = original.VatRate,
                    VatAmount = -original.VatAmount,
                    PaymentMethod = original.PaymentMethod,
                    Date = DateOnly.FromDateTime(now.UtcDateTime),
                    Beneficiary = original.Beneficiary,
                    ReversalOfId = original.Id
                });
                reversed = true;
            }
        }

        fiche.CancelledAt = now;
        fiche.CancelledByUserId = actorId;
        fiche.CancellationReason = reason.Trim();
        return reversed;
    }
}
