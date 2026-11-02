using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Attendance.GetAttendanceSheet;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Boarding.Documents.DormitoryRegister;

/// <summary>Ligne du registre : un interne et son pointage de la nuit (vide tant que rien n'est pointé).</summary>
public record DormitoryRegisterRowDto(
    string RoomName,
    int BedNumber,
    string StudentName,
    string? StudentNameAr,
    string Matricule,
    string ClassroomName,
    bool OnLeave,
    bool Recorded,
    bool? IsPresent,
    string? Note);

/// <summary>
/// Données d'impression du registre d'un pavillon pour UNE nuit. Document posé dans le pavillon : AUCUNE donnée de santé et
/// AUCUN numéro de téléphone (spec §5.2) — le nom du surveillant y figure, pas son téléphone. Un test par réflexion verrouille
/// cette forme (<c>DormitoryRegisterDtoShapeTests</c>) : ne pas y ajouter de champ sensible.
/// </summary>
public record DormitoryRegisterDto(
    string SchoolName,
    string? SchoolAddress,
    string? SchoolLogoUrl,
    string? SchoolYearLabel,
    string DormitoryName,
    string? SupervisorName,
    DateOnly Date,
    IReadOnlyList<DormitoryRegisterRowDto> Rows,
    DateTimeOffset GeneratedAt,
    string? SessionName = null);

/// <summary>Registre d'un pavillon pour la nuit <paramref name="Date"/> (aujourd'hui par défaut). Mêmes règles de date que le pointage.</summary>
public record GetDormitoryRegisterQuery(Guid DormitoryId, DateOnly? Date, Guid? SessionId = null) : IRequest<DormitoryRegisterDto>;

public class GetDormitoryRegisterQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<GetDormitoryRegisterQuery, DormitoryRegisterDto>
{
    public async Task<DormitoryRegisterDto> Handle(GetDormitoryRegisterQuery request, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var date = request.Date ?? today;

        // Même définition que l'écran de pointage : 404 (pavillon inconnu), 422 (date future / hors année), pré-remplissages.
        var sheet = await AttendanceSheetReader.GetAsync(dbContext, request.DormitoryId, date, request.SessionId, today, cancellationToken);

        var dormitory = await dbContext.Dormitories.AsNoTracking()
            .Where(d => d.Id == request.DormitoryId)
            .Select(d => new { d.SchoolId, d.SupervisorName })
            .FirstAsync(cancellationToken);

        var school = await dbContext.Schools.AsNoTracking()
            .Where(s => s.Id == dormitory.SchoolId)
            .Select(s => new { s.Name, s.Address, s.LogoUrl })
            .FirstAsync(cancellationToken);

        var yearLabel = await dbContext.SchoolYears.AsNoTracking()
            .Where(y => y.IsActive)
            .Select(y => y.Label)
            .FirstOrDefaultAsync(cancellationToken);

        var ids = sheet.Lines.Select(l => l.BoarderId).ToList();
        var extras = await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                            join s in dbContext.Students on be.StudentId equals s.Id
                            join e in dbContext.Enrollments on be.EnrollmentId equals e.Id
                            where ids.Contains(be.Id)
                            select new
                            {
                                be.Id,
                                s.FullNameAr,
                                // Une classe supprimée sort du filtre : on l'affiche explicitement (même principe que BoarderReader).
                                ClassroomName = dbContext.Classrooms.IgnoreQueryFilters()
                                    .Where(c => c.Id == e.ClassroomId && c.SchoolId == e.SchoolId)
                                    .Select(c => c.Name)
                                    .FirstOrDefault() ?? "Classe supprimée"
                            })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var rows = sheet.Lines.Select(l => new DormitoryRegisterRowDto(
                l.RoomName, l.BedNumber, l.StudentName, extras[l.BoarderId].FullNameAr, l.Matricule,
                extras[l.BoarderId].ClassroomName, l.OnLeave, l.Recorded, l.IsPresent, l.Note))
            .ToList();

        return new DormitoryRegisterDto(
            school.Name, school.Address, school.LogoUrl, yearLabel, sheet.DormitoryName, dormitory.SupervisorName,
            date, rows, timeProvider.GetUtcNow(), sheet.SessionName);
    }
}
