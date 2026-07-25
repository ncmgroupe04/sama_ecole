using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetEntryTicket;

/// <summary>
/// Données d'un billet d'entrée, dérivées d'un retard (<c>LateArrival</c>) déjà enregistré par la
/// Surveillance. Le billet n'est pas une entité à part : c'est l'impression officielle d'un retard,
/// ce qui lui donne un numéro stable (dérivé de l'identifiant du retard) et garantit la traçabilité
/// (le retard reste historisé, jamais supprimé physiquement).
/// </summary>
public record GetEntryTicketQuery(Guid LateArrivalId) : IRequest<EntryTicketDto>;

public record EntryTicketDto(
    Guid LateArrivalId,
    string TicketNumber,
    string StudentFullName,
    string Matricule,
    string ClassroomName,
    string ClassroomLevel,
    DateTime Date,
    int Minutes,
    string Reason,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl);

public class GetEntryTicketQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetEntryTicketQuery, EntryTicketDto>
{
    public async Task<EntryTicketDto> Handle(GetEntryTicketQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from l in dbContext.LateArrivals.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on l.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on l.SchoolId equals sch.Id
            where l.Id == request.LateArrivalId
            select new
            {
                l.Id,
                s.Matricule,
                StudentFullName = s.FullName,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                l.Date,
                l.Minutes,
                l.Reason,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                SchoolPhone = sch.Phone,
                SchoolEmail = sch.Email,
                sch.Ninea,
                sch.RegistreCommerce,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Retard introuvable : {request.LateArrivalId}");

        return new EntryTicketDto(
            row.Id,
            $"BILLET-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.StudentFullName,
            row.Matricule,
            row.ClassroomName,
            row.ClassroomLevel,
            row.Date,
            row.Minutes,
            row.Reason,
            row.SchoolName,
            row.SchoolAddress,
            row.SchoolPhone,
            row.SchoolEmail,
            ReceiptCity.FromAddress(row.SchoolAddress),
            row.Ninea,
            row.RegistreCommerce,
            row.LogoUrl);
    }
}
