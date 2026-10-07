using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Assignments;

/// <summary>
/// Règle d'affectation d'un élève à l'internat, écrite UNE seule fois (spec Internat Pavillon/Lit, lot C) : utilisée par
/// les commandes <c>assign-bed</c>/<c>unassign-bed</c> ET par les adaptateurs de l'ancien écran
/// (<c>ChangeBoardingAssignment</c>, <c>CreateEnrollment</c>). Aucune méthode n'appelle <c>SaveChanges</c> : l'appelant
/// décide de la transaction (l'inscription, par exemple, enregistre séjour et dossier ensemble) et passe par
/// <see cref="BoardingConflicts.SaveAsync"/> pour traduire la course sur un lit en 409 <c>BED_UNAVAILABLE</c>.
/// </summary>
public interface IBoardingAssignmentService
{
    /// <summary>
    /// Crée ou transfère le séjour ACTIF de l'inscription (régime + lit). <paramref name="roomId"/> sert à l'ancien
    /// écran, qui envoie une chambre et non un lit : le plus petit numéro de lit libre et disponible est alors choisi
    /// (ou le lit actuel si l'élève est déjà dans cette chambre). Un demi-pensionnaire n'a jamais de lit : une chambre
    /// éventuelle est ignorée. <paramref name="requireStayRowVersion"/> impose le jeton du séjour lors d'un transfert.
    /// </summary>
    Task<BoardingEnrollment> AssignAsync(
        Enrollment enrollment, BoardingRegime regime, Guid? bedId, Guid? roomId,
        uint? stayRowVersion, bool requireStayRowVersion, CancellationToken cancellationToken);

    /// <summary>
    /// Clôt le séjour actif de l'inscription (<c>EndDate</c> = aujourd'hui, lit libéré). Retourne null s'il n'y en a pas.
    /// Refusé en 409 <c>LEAVE_IN_PROGRESS</c> tant qu'une sortie est ouverte.
    /// </summary>
    Task<BoardingEnrollment?> EndAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>
    /// Ajoute les lignes de pension MANQUANTES de l'inscription et incrémente <c>TotalDue</c> : jamais de doublon, jamais
    /// de retrait (une fin de séjour ou un transfert ne retranche rien — décision #7 de la spec du 18/09).
    /// </summary>
    Task AddMissingBoardingFeeAsync(Enrollment enrollment, CancellationToken cancellationToken);
}
