using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace SamaEcole.Application.Schools.Commands.SetMatriculeSequenceStart;

/// <summary>
/// Règle le compteur <c>{SEQ}</c> des matricules pour l'année scolaire en cours (voir la commande).
///
/// Le compteur est stocké dans <c>matricule_sequences</c> sous la forme « dernier numéro attribué »
/// (<see cref="MatriculeSequence.LastValue"/>) : le prochain matricule vaudra <c>LastValue + 1</c>
/// (MatriculeGenerator). Pour que le prochain soit <c>NextValue</c>, on pose donc
/// <c>LastValue = NextValue - 1</c>.
///
/// Aucun verrou optimiste : c'est un réglage d'administration à faible contention, et la seule
/// écriture concurrente qui compte — une génération de matricule en parallèle — est déjà sérialisée
/// par l'index unique <c>(SchoolId, Kind, Year)</c> côté MatriculeGenerator. Le vrai garde-fou est
/// le refus de rétrograder le compteur.
/// </summary>
public class SetMatriculeSequenceStartCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    TimeProvider timeProvider,
    ILogger<SetMatriculeSequenceStartCommandHandler> logger)
    : IRequestHandler<SetMatriculeSequenceStartCommand, MatriculeSequenceInfo>
{
    public async Task<MatriculeSequenceInfo> Handle(
        SetMatriculeSequenceStartCommand request,
        CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Année SCOLAIRE en cours (bascule d'octobre) — le même millésime que celui gravé dans les
        // matricules générés en ce moment.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());

        var sequence = await dbContext.MatriculeSequences
            .FirstOrDefaultAsync(
                s => s.SchoolId == schoolId && s.Kind == request.Kind && s.Year == year,
                cancellationToken);

        var currentLastValue = sequence?.LastValue ?? 0;

        // Rétrograder le compteur réémettrait des numéros déjà en circulation. NextValue peut
        // égaler currentLastValue + 1 (report sans effet) ou le dépasser (on réserve une plage),
        // jamais être inférieur ou égal au dernier numéro déjà sorti.
        if (request.NextValue <= currentLastValue)
        {
            throw new BusinessRuleException(
                $"Des matricules ont déjà été attribués jusqu'au numéro {currentLastValue} pour l'année {year}. "
                + $"Le prochain numéro doit être strictement supérieur à {currentLastValue} pour ne pas en réémettre un.");
        }

        var newLastValue = request.NextValue - 1;

        if (sequence is null)
        {
            sequence = new MatriculeSequence
            {
                SchoolId = schoolId,
                Kind = request.Kind,
                Year = year,
                LastValue = newLastValue
            };
            dbContext.MatriculeSequences.Add(sequence);
        }
        else
        {
            sequence.LastValue = newLastValue;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Compteur de matricules {Kind} de l'établissement {SchoolId} réglé : prochain numéro {NextValue} pour l'année {Year}.",
            request.Kind, schoolId, request.NextValue, year);

        return new MatriculeSequenceInfo(request.Kind.ToString(), year, newLastValue, request.NextValue);
    }
}
