using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance;

/// <summary>Règles partagées par la feuille d'appel (lecture) et le pointage collectif (écriture).</summary>
internal static class AttendanceRules
{
    /// <summary>Note posée à un pensionnaire absent parce qu'il est en sortie, quand l'opérateur n'en saisit pas.</summary>
    public const string OnLeaveNote = "En permission";

    /// <summary>Nom de la session créée à la volée quand une école pointe « la nuit » sans en avoir configuré (routes d'origine).</summary>
    public const string DefaultNightSessionName = "Appel de nuit";

    /// <summary>Séjours pointables d'un pavillon : actifs, internes, avec un lit de ce pavillon (la demi-pension ne dort pas sur place).</summary>
    public static IQueryable<BoardingEnrollment> DormitoryStays(IApplicationDbContext dbContext, Guid dormitoryId) =>
        from be in dbContext.BoardingEnrollments
        join bed in dbContext.Beds on be.BedId equals bed.Id
        join room in dbContext.DormitoryRooms on bed.DormitoryRoomId equals room.Id
        where be.IsActive && be.Regime == BoardingRegime.Interne && room.DormitoryId == dormitoryId
        select be;

    /// <summary>« Jour pointable » : pas dans le futur, et dans l'année scolaire active.</summary>
    public static async Task EnsurePointableAsync(
        IApplicationDbContext dbContext, DateOnly date, DateOnly today, CancellationToken cancellationToken)
    {
        if (date > today)
        {
            throw new ValidationException([new ValidationFailure("Date", "Le pointage ne peut pas porter sur une date future.")]);
        }

        var inActiveYear = await dbContext.SchoolYears.AsNoTracking()
            .AnyAsync(y => y.IsActive && y.StartDate <= date && y.EndDate >= date, cancellationToken);

        if (!inActiveYear)
        {
            throw new ValidationException(
                [new ValidationFailure("Date", "La date doit appartenir à l'année scolaire active.")]);
        }
    }

    public static async Task<Dormitory> GetDormitoryAsync(
        IApplicationDbContext dbContext, Guid dormitoryId, CancellationToken cancellationToken) =>
        await dbContext.Dormitories.AsNoTracking().FirstOrDefaultAsync(d => d.Id == dormitoryId, cancellationToken)
        ?? throw new KeyNotFoundException($"Pavillon {dormitoryId} introuvable.");

    /// <summary>Séjours (parmi <paramref name="stayIds"/>) dont le pensionnaire dort dehors la nuit <paramref name="night"/>.</summary>
    public static async Task<HashSet<Guid>> OnLeaveAsync(
        IApplicationDbContext dbContext, List<Guid> stayIds, DateOnly night, CancellationToken cancellationToken) =>
        (await dbContext.BoardingLeaves.AsNoTracking()
            .Where(BoardingLeaveStatus.OnNight(night))
            .Where(l => stayIds.Contains(l.BoardingEnrollmentId))
            .Select(l => l.BoardingEnrollmentId)
            .ToListAsync(cancellationToken)).ToHashSet();

    /// <summary>
    /// Session visée par une feuille de pavillon. Identifiant donné : cette session, qui doit s'adresser aux internes. Sans
    /// identifiant (routes d'origine) : la première session des internes — l'« Appel de nuit » repris par la migration. Une école
    /// qui n'en a aucune obtient <c>null</c> (rien n'a pu être pointé).
    /// </summary>
    public static async Task<AttendanceSession?> FindInternSessionAsync(
        IApplicationDbContext dbContext, Guid? sessionId, bool tracking, CancellationToken cancellationToken)
    {
        var sessions = tracking ? dbContext.AttendanceSessions : dbContext.AttendanceSessions.AsNoTracking();

        if (sessionId is { } id)
        {
            var session = await sessions.FirstOrDefaultAsync(s => s.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException($"Session de pointage {id} introuvable.");

            if (session.Audience != AttendanceAudience.Interns)
            {
                throw new ValidationException(
                    [new ValidationFailure("SessionId", "Cette session ne s'adresse pas aux internes d'un pavillon : choisissez son groupe (classe ou Oustaz).")]);
            }

            return session;
        }

        return await sessions
            .Where(s => s.Audience == AttendanceAudience.Interns)
            .OrderByDescending(s => s.IsActive).ThenBy(s => s.SortOrder).ThenBy(s => s.Name)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Une session désactivée ne reçoit plus de pointage (elle reste lisible : registre, récapitulatifs).</summary>
    public static void EnsureActive(AttendanceSession session)
    {
        if (!session.IsActive)
        {
            throw new BusinessRuleException(
                $"La session « {session.Name} » est désactivée : réactivez-la dans la configuration pour pointer.", "ATTENDANCE_SESSION_INACTIVE");
        }
    }
}
