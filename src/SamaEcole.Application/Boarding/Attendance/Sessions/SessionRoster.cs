using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// Qui pointe-t-on, pour quelle session et quel groupe ? Définition UNIQUE partagée par la feuille d'appel, le pointage et le
/// récapitulatif mensuel : les trois lisent la même liste d'élèves.
/// <list type="bullet">
/// <item><c>HalfBoarders</c> / <c>AllBoarders</c> : les pensionnaires (séjour actif) de la CLASSE ; les premiers sont les seuls
/// demi-pensionnaires.</item>
/// <item><c>Halqa</c> : les élèves de la Halqa de l'Oustaz (<see cref="Student.InstructorId"/>), pensionnaires ou non.</item>
/// </list>
/// Les internes d'un pavillon ont leur propre feuille (<c>GetAttendanceSheetQuery</c>).
/// </summary>
internal static class SessionRoster
{
    public sealed record Member(Guid StudentId, string FullName, string? FullNameAr, string Matricule, string? Detail, Guid? StayId);

    public sealed record Roster(Guid GroupId, string GroupName, IReadOnlyList<Member> Members);

    /// <summary>Session visée par une feuille de groupe (hors pavillon) : elle doit exister et ne pas être celle des internes d'un pavillon.</summary>
    public static async Task<AttendanceSession> GetGroupSessionAsync(
        IApplicationDbContext dbContext, Guid sessionId, bool tracking, CancellationToken cancellationToken)
    {
        var sessions = tracking ? dbContext.AttendanceSessions : dbContext.AttendanceSessions.AsNoTracking();
        var session = await sessions.FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Session de pointage {sessionId} introuvable.");

        if (session.Audience == AttendanceAudience.Interns)
        {
            throw new ValidationException(
                [new ValidationFailure("SessionId", "Cette session s'adresse aux internes d'un pavillon : utilisez la feuille du pavillon.")]);
        }

        return session;
    }

    /// <summary>
    /// Groupe effectivement pointé, selon qui demande. Un Oustaz ne pointe QUE sa Halqa (le groupe est alors imposé) ; les autres
    /// rôles désignent le groupe (classe, ou Oustaz pour une session de Halqa). Un Oustaz sur une session de classe : refusé.
    /// </summary>
    public static async Task<Guid> ResolveGroupAsync(
        HalqaScopeAuthorizer scope, AttendanceSession session, Guid? requestedGroupId, CancellationToken cancellationToken)
    {
        var own = await scope.GetOwnInstructorIdOrNullAsync(cancellationToken);

        if (own is not null)
        {
            if (session.Audience != AttendanceAudience.Halqa)
            {
                throw new ForbiddenException("Cette session n'est pas celle d'une Halqa : seule la Direction la pointe.");
            }

            if (requestedGroupId is { } requested && requested != own)
            {
                throw new ForbiddenException("Cette Halqa n'est pas la vôtre : vous ne pouvez pointer que vos propres élèves.");
            }

            return own.Value;
        }

        return requestedGroupId
            ?? throw new ValidationException([new ValidationFailure(
                "GroupId", session.Audience == AttendanceAudience.Halqa ? "Choisissez l'Oustaz." : "Choisissez la classe.")]);
    }

    /// <summary>Session visée par un récapitulatif : n'importe quelle audience (la feuille d'un pavillon comme celles des groupes).</summary>
    public static async Task<AttendanceSession> GetAnySessionAsync(
        IApplicationDbContext dbContext, Guid sessionId, CancellationToken cancellationToken) =>
        await dbContext.AttendanceSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
        ?? throw new KeyNotFoundException($"Session de pointage {sessionId} introuvable.");

    public static async Task<Roster> LoadAsync(
        IApplicationDbContext dbContext, AttendanceSession session, Guid groupId, CancellationToken cancellationToken)
    {
        if (session.Audience == AttendanceAudience.Interns)
        {
            var dormitory = await AttendanceRules.GetDormitoryAsync(dbContext, groupId, cancellationToken);

            var interns = await (from be in AttendanceRules.DormitoryStays(dbContext, groupId).AsNoTracking()
                                 join s in dbContext.Students on be.StudentId equals s.Id
                                 join bed in dbContext.Beds on be.BedId equals bed.Id
                                 join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
                                 orderby room.Name, bed.BedNumber
                                 select new { be.Id, StudentId = s.Id, s.FullName, s.FullNameAr, s.Matricule, RoomName = room.Name, bed.BedNumber })
                .ToListAsync(cancellationToken);

            return new Roster(
                groupId, dormitory.Name,
                interns.Select(i => new Member(i.StudentId, i.FullName, i.FullNameAr, i.Matricule, $"{i.RoomName} · lit {i.BedNumber}", i.Id)).ToList());
        }

        if (session.Audience == AttendanceAudience.Halqa)
        {
            var instructor = await dbContext.Instructors.AsNoTracking()
                .Where(i => i.Id == groupId).Select(i => new { i.FullName }).FirstOrDefaultAsync(cancellationToken)
                ?? throw new KeyNotFoundException($"Oustaz {groupId} introuvable.");

            var students = await dbContext.Students.AsNoTracking()
                .Where(s => s.InstructorId == groupId)
                .OrderBy(s => s.FullName)
                .Select(s => new Member(s.Id, s.FullName, s.FullNameAr, s.Matricule, null, null))
                .ToListAsync(cancellationToken);

            return new Roster(groupId, instructor.FullName, students);
        }

        var classroom = await dbContext.Classrooms.AsNoTracking()
            .Where(c => c.Id == groupId).Select(c => new { c.Name }).FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Classe {groupId} introuvable.");

        var halfOnly = session.Audience == AttendanceAudience.HalfBoarders;
        var boarders = await (from be in dbContext.BoardingEnrollments.AsNoTracking()
                              join s in dbContext.Students on be.StudentId equals s.Id
                              where be.IsActive && s.ClassroomId == groupId
                                    && (!halfOnly || be.Regime == BoardingRegime.DemiPensionnaire)
                              orderby s.FullName
                              select new { be.Id, be.Regime, StudentId = s.Id, s.FullName, s.FullNameAr, s.Matricule })
            .ToListAsync(cancellationToken);

        var members = boarders.Select(b => new Member(
                b.StudentId, b.FullName, b.FullNameAr, b.Matricule,
                halfOnly ? null : b.Regime == BoardingRegime.Interne ? "Interne" : "Demi-pensionnaire", b.Id))
            .ToList();

        return new Roster(groupId, classroom.Name, members);
    }
}
