using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Boarders;

/// <summary>
/// Lecture d'une fiche de pensionnaire avec le masquage de la fiche médicale PAR RÔLE : une seule règle, partagée par la
/// lecture et le retour de l'écriture du profil, pour qu'aucun chemin ne renvoie les notes médicales à un rôle qui n'y a
/// pas droit.
/// </summary>
internal static class BoarderDetailReader
{
    public static bool CanReadMedical(Role? role) => role is Role.Directeur or Role.Surveillant;

    public static async Task<BoarderDetailDto> GetAsync(
        IApplicationDbContext dbContext, Guid id, Role? role, CancellationToken cancellationToken)
    {
        var stay = await dbContext.BoardingEnrollments.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour {id} introuvable.");

        var item = await BoarderReader.GetItemAsync(dbContext, id, cancellationToken);

        return new BoarderDetailDto(
            item,
            CanReadMedical(role) ? stay.MedicalNotes : null,
            stay.EmergencyContactName,
            stay.EmergencyContactPhone,
            stay.AllowedExitPersons.Select(p => new AllowedExitPersonDto(p.Name, p.Relationship, p.Phone)).ToList());
    }
}
