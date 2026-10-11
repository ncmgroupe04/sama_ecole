using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Beds;

/// <summary>
/// Lecture des lits d'une chambre avec leur statut CALCULÉ (spec N2) et l'occupant éventuel. Une seule définition
/// de « lit + statut » pour les commandes de chambre, de lit et les requêtes de lecture.
/// </summary>
public static class BedReader
{
    public static async Task<IReadOnlyList<BedDto>> ListForRoomAsync(
        IApplicationDbContext dbContext, Guid roomId, CancellationToken cancellationToken)
    {
        var activeBeds = BoardingOccupancy.ActiveBedIds(dbContext);

        var rows = await (from b in dbContext.Beds.AsNoTracking()
                          where b.DormitoryRoomId == roomId
                          orderby b.BedNumber
                          select new
                          {
                              b.Id,
                              b.DormitoryRoomId,
                              b.BedNumber,
                              b.Status,
                              Occupied = activeBeds.Contains(b.Id),
                              BoarderId = dbContext.BoardingEnrollments
                                  .Where(be => be.IsActive && be.BedId == b.Id)
                                  .Select(be => (Guid?)be.Id).FirstOrDefault(),
                              OccupantName = dbContext.BoardingEnrollments
                                  .Where(be => be.IsActive && be.BedId == b.Id)
                                  .Select(be => dbContext.Students.Where(s => s.Id == be.StudentId)
                                      .Select(s => s.FullName).FirstOrDefault())
                                  .FirstOrDefault(),
                              RowVersion = EF.Property<uint>(b, "xmin")
                          }).ToListAsync(cancellationToken);

        return rows.Select(x => new BedDto(
            x.Id, x.DormitoryRoomId, x.BedNumber, BoardingOccupancy.StatusOf(x.Status, x.Occupied),
            x.BoarderId, x.OccupantName, x.RowVersion)).ToList();
    }
}
