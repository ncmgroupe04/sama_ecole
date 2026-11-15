using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Absences.Queries.GetAbsenceTicket;

/// <summary>
/// Billet d'absence A5, pendant des billets d'entrée et de sortie, pour une absence de plusieurs jours déjà
/// enregistrée (<c>AbsenceJustification</c>). Remis à la famille / versé au dossier de l'élève.
/// </summary>
public record GetAbsenceTicketQuery(Guid AbsenceJustificationId) : IRequest<AbsenceTicketDto>;

public record AbsenceTicketDto(
    Guid AbsenceJustificationId,
    string TicketNumber,
    string StudentFullName,
    string Matricule,
    string ClassroomName,
    string ClassroomLevel,
    DateOnly StartDate,
    DateOnly EndDate,
    int Days,
    string Reason,
    DateOnly? AuthorizedReturnDate,
    DateOnly IssuedOn,
    string SchoolName,
    string? SchoolAddress,
    string? SchoolPhone,
    string? SchoolEmail,
    string? SchoolCity,
    string? SchoolNinea,
    string? SchoolRegistreCommerce,
    string? SchoolLogoUrl,
    string? SurveillantSignatureUrl);

public class GetAbsenceTicketQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetAbsenceTicketQuery, AbsenceTicketDto>
{
    public async Task<AbsenceTicketDto> Handle(GetAbsenceTicketQuery request, CancellationToken cancellationToken)
    {
        var row = await (
            from a in dbContext.AbsenceJustifications.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on a.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on s.ClassroomId equals c.Id
            join sch in dbContext.Schools.AsNoTracking() on a.SchoolId equals sch.Id
            where a.Id == request.AbsenceJustificationId
            select new
            {
                a.Id,
                s.Matricule,
                StudentFullName = s.FullName,
                ClassroomName = c.Name,
                ClassroomLevel = c.Level,
                a.StartDate,
                a.EndDate,
                a.Reason,
                a.AuthorizedReturnDate,
                a.CreatedAt,
                SchoolName = sch.Name,
                SchoolAddress = sch.Address,
                SchoolPhone = sch.Phone,
                SchoolEmail = sch.Email,
                sch.Ninea,
                sch.RegistreCommerce,
                sch.LogoUrl
            }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Billet d'absence introuvable : {request.AbsenceJustificationId}");

        // Global Query Filter + RLS bornent déjà cette lecture à l'école courante : au plus une ligne de réglages.
        var surveillantSignatureUrl = await dbContext.SchoolSettings.AsNoTracking()
            .Select(set => set.SurveillantSignatureUrl)
            .FirstOrDefaultAsync(cancellationToken);

        return new AbsenceTicketDto(
            row.Id,
            $"ABSENCE-{row.Id.ToString()[..8].ToUpperInvariant()}",
            row.StudentFullName,
            row.Matricule,
            row.ClassroomName,
            row.ClassroomLevel,
            row.StartDate,
            row.EndDate,
            row.EndDate.DayNumber - row.StartDate.DayNumber + 1,
            row.Reason,
            row.AuthorizedReturnDate,
            DateOnly.FromDateTime(row.CreatedAt.UtcDateTime),
            row.SchoolName,
            row.SchoolAddress,
            row.SchoolPhone,
            row.SchoolEmail,
            ReceiptCity.FromAddress(row.SchoolAddress),
            row.Ninea,
            row.RegistreCommerce,
            row.LogoUrl,
            surveillantSignatureUrl);
    }
}
