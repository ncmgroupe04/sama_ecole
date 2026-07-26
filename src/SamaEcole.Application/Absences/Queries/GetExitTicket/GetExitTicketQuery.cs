using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetExitTicket;

/// <summary>
/// Billet de sortie A5, pendant du billet d'entrée (<see cref="SamaEcole.Application.Absences.Queries.GetEntryTicket.EntryTicketDto"/>)
/// pour une sortie anticipée déjà enregistrée par la Surveillance.
/// </summary>
public record GetExitTicketQuery(Guid EarlyDepartureId) : IRequest<ExitTicketDto>;

public record ExitTicketDto(
    Guid EarlyDepartureId,
    string TicketNumber,
    string StudentFullName,
    string Matricule,
    string ClassroomName,
    string ClassroomLevel,
    DateTime Date,
    TimeOnly DepartureTime,
    string Reason,
    string? PickedUpBy,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl);

public class GetExitTicketQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetExitTicketQuery, ExitTicketDto>
{
    public async Task<ExitTicketDto> Handle(GetExitTicketQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from d in dbContext.EarlyDepartures.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on d.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on d.SchoolId equals sch.Id
            where d.Id == request.EarlyDepartureId
            select new
            {
                d.Id,
                s.Matricule,
                StudentFullName = s.FullName,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                d.Date,
                d.DepartureTime,
                d.Reason,
                d.PickedUpBy,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                SchoolPhone = sch.Phone,
                SchoolEmail = sch.Email,
                sch.Ninea,
                sch.RegistreCommerce,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Sortie anticipée introuvable : {request.EarlyDepartureId}");

        return new ExitTicketDto(
            row.Id,
            $"SORTIE-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.StudentFullName,
            row.Matricule,
            row.ClassroomName,
            row.ClassroomLevel,
            row.Date,
            row.DepartureTime,
            row.Reason,
            row.PickedUpBy,
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
