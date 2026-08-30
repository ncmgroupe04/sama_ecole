using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Common;

namespace SamaEcole.Persistence;

/// <summary>
/// Génération ALGORITHMIQUE DE SECOURS d'un IEN (ticket JGK-M01, Volume 1 §23.1).
///
/// ⚠ CE QUE CETTE CLASSE PRODUIT N'EST PAS UN IEN OFFICIEL. Voir la mise en garde complète sur
/// <see cref="IIenGeneratorService"/>. Tout numéro sorti d'ici est marqué
/// <c>Student.IsIenProvisional = true</c> et signalé comme tel dans l'export Planète.
///
/// La FORME du numéro (préfixe <c>P</c>, clé de contrôle Luhn, longueur) et le contrôle de forme d'un
/// IEN saisi vivent dans <see cref="IenNumberFormat"/> (Application) : c'est un concept de contrat,
/// partagé avec le validateur d'IEN et les tests. Cette classe n'ajoute que la SÉQUENCE issue de la
/// base — sérialisée par PostgreSQL via le compteur de <see cref="MatriculeGenerator"/>, et annulée
/// avec la transaction en cas de rollback (aucun trou).
///
/// LE JOUR OÙ LE FORMAT NATIONAL RÉEL SERA CONNU : ne pas modifier <see cref="IenNumberFormat"/> pour
/// l'imiter. Les numéros officiels viendront du SIMEN
/// (<see cref="ISimenBridgeService.LookupOfficialIensAsync"/>) et écraseront les provisoires. Faire
/// ressembler nos numéros aux leurs ne ferait que rendre les deux indiscernables, au moment précis où
/// la distinction compte le plus.
/// </summary>
public class NationalIenGenerator(
    ApplicationDbContext dbContext,
    MatriculeGenerator matriculeGenerator,
    TimeProvider timeProvider) : IIenGeneratorService
{
    public async Task<string> GenerateProvisionalIenAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + filtre explicite : même raison que dans MatriculeGenerator — le
        // générateur peut être appelé hors d'un contexte où le filtre global est satisfait.
        var schoolCode = await dbContext.Schools
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(s => s.Id == schoolId && !s.IsDeleted)
            .Select(s => s.NationalSchoolCode)
            .FirstOrDefaultAsync(cancellationToken);

        // REFUS, jamais un repli sur un code arbitraire. Un IEN dont les six chiffres d'établissement
        // sont inventés rattacherait l'élève à une AUTRE école dans tout fichier qui le lirait.
        if (string.IsNullOrWhiteSpace(schoolCode))
        {
            throw new InvalidOperationException(
                "Impossible de générer un IEN provisoire : le code établissement national (SIMEN) "
                + "n'est pas renseigné dans Paramètres → Établissement.");
        }

        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var sequence = await matriculeGenerator.NextProvisionalIenSequenceAsync(schoolId, year, cancellationToken);

        return IenNumberFormat.ComposeProvisional(schoolCode, year, sequence);
    }

    public bool IsWellFormed(string ienNumber) => IenNumberFormat.IsWellFormed(ienNumber);
}
