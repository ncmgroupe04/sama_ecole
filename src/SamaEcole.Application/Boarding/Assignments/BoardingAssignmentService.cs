using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Assignments;

public class BoardingAssignmentService(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IBoardingAssignmentService
{
    public async Task<BoardingEnrollment> AssignAsync(
        Enrollment enrollment, BoardingRegime regime, Guid? bedId, Guid? roomId,
        uint? stayRowVersion, bool requireStayRowVersion, CancellationToken cancellationToken)
    {
        // Garde de forme AVANT toute lecture : JsonStringEnumConverter accepte une valeur d'enum hors domaine
        // ({"regime": 99}), qui franchirait les comparaisons ci-dessous et serait persistée telle quelle.
        if (!Enum.IsDefined(regime))
        {
            throw Invalid("Regime", "Le régime d'hébergement indiqué n'est pas valide.");
        }

        var stay = await dbContext.BoardingEnrollments
            .FirstOrDefaultAsync(b => b.EnrollmentId == enrollment.Id && b.IsActive, cancellationToken);

        Bed? bed = null;

        if (regime == BoardingRegime.Interne)
        {
            if (bedId is { } explicitBed)
            {
                bed = await dbContext.Beds.FirstOrDefaultAsync(b => b.Id == explicitBed, cancellationToken)
                    ?? throw Invalid("BedId", "Le lit indiqué n'existe pas dans votre établissement.");
            }
            else if (roomId is { } room)
            {
                bed = await FindFreeBedAsync(room, stay, cancellationToken);
            }
            else
            {
                throw Invalid("BedId", "Un lit est requis pour un régime Interne.");
            }

            if (bed.Status == BedStatus.Maintenance)
            {
                throw Invalid("BedId", "Ce lit est en maintenance.");
            }

            await EnsureGenderAsync(enrollment.StudentId, bed, cancellationToken);

            var stayId = stay?.Id;
            if (await dbContext.BoardingEnrollments.AnyAsync(
                    b => b.BedId == bed.Id && b.IsActive && b.Id != stayId, cancellationToken))
            {
                throw new BusinessRuleException("Ce lit est déjà occupé.", BoardingConflicts.BedUnavailable);
            }
        }
        else if (bedId is not null)
        {
            throw Invalid("BedId", "Un demi-pensionnaire n'occupe pas de lit.");
        }

        // DemiPensionnaire : une chambre éventuellement envoyée par l'ancien écran est ignorée (le demi-pensionnaire ne
        // dort pas à l'internat, spec N7).
        if (stay is null)
        {
            stay = new BoardingEnrollment
            {
                SchoolId = enrollment.SchoolId,
                StudentId = enrollment.StudentId,
                EnrollmentId = enrollment.Id,
                Regime = regime,
                BedId = bed?.Id,
                StartDate = Today(),
                IsActive = true
            };
            dbContext.BoardingEnrollments.Add(stay);
        }
        else
        {
            if (requireStayRowVersion && stayRowVersion is null)
            {
                throw Invalid("RowVersion", "Le jeton de version du séjour est requis pour un transfert.");
            }

            if (stayRowVersion is { } token)
            {
                dbContext.SetOriginalConcurrencyToken(stay, token);
            }

            stay.Regime = regime;
            stay.BedId = bed?.Id;
        }

        return stay;
    }

    public async Task<BoardingEnrollment?> EndAsync(Guid enrollmentId, CancellationToken cancellationToken)
    {
        var stay = await dbContext.BoardingEnrollments
            .FirstOrDefaultAsync(b => b.EnrollmentId == enrollmentId && b.IsActive, cancellationToken);

        if (stay is null)
        {
            return null;
        }

        if (await dbContext.BoardingLeaves.AnyAsync(
                l => l.BoardingEnrollmentId == stay.Id && l.ActualReturnDate == null, cancellationToken))
        {
            throw new BusinessRuleException(
                "Enregistrez d'abord le retour de la sortie en cours.", "LEAVE_IN_PROGRESS");
        }

        var today = Today();
        stay.IsActive = false;
        stay.EndDate = today < stay.StartDate ? stay.StartDate : today;   // CK_boarding_enrollments_dates
        stay.BedId = null;

        return stay;
    }

    public async Task AddMissingBoardingFeeAsync(Enrollment enrollment, CancellationToken cancellationToken)
    {
        var existingFeeCategoryIds = await dbContext.EnrollmentFeeLines.AsNoTracking()
            .Where(l => l.EnrollmentId == enrollment.Id)
            .Select(l => l.FeeCategoryId)
            .ToListAsync(cancellationToken);

        var tuitionMonths = await ResolveTuitionMonthsAsync(enrollment.SchoolId, cancellationToken);
        var newLines = await BoardingFeeLineBuilder.BuildMissingBoardingLinesAsync(
            dbContext, enrollment.SchoolId, enrollment.ClassroomId, tuitionMonths,
            existingFeeCategoryIds.ToHashSet(), cancellationToken);

        foreach (var line in newLines)
        {
            line.EnrollmentId = enrollment.Id;
            dbContext.EnrollmentFeeLines.Add(line);
            enrollment.TotalDue += line.LineTotal;
        }
    }

    /// <summary>
    /// Lit libre d'une chambre pour l'ancien écran : le lit ACTUEL si l'élève est déjà dans cette chambre (rien ne
    /// bouge), sinon le plus petit numéro de lit disponible et non tenu par un séjour actif.
    /// </summary>
    private async Task<Bed> FindFreeBedAsync(Guid roomId, BoardingEnrollment? stay, CancellationToken cancellationToken)
    {
        if (!await dbContext.DormitoryRooms.AnyAsync(r => r.Id == roomId, cancellationToken))
        {
            throw Invalid("RoomId", "La chambre indiquée n'existe pas dans votre établissement.");
        }

        if (stay?.BedId is { } currentBedId)
        {
            var current = await dbContext.Beds
                .FirstOrDefaultAsync(b => b.Id == currentBedId && b.DormitoryRoomId == roomId, cancellationToken);

            if (current is not null)
            {
                return current;
            }
        }

        var heldBeds = BoardingOccupancy.ActiveBedIds(dbContext);

        var free = await dbContext.Beds
            .Where(b => b.DormitoryRoomId == roomId && b.Status == BedStatus.Available && !heldBeds.Contains(b.Id))
            .OrderBy(b => b.BedNumber)
            .FirstOrDefaultAsync(cancellationToken);

        return free ?? throw Invalid("RoomId", "Cette chambre a atteint sa capacité maximale.");
    }

    /// <summary>Élève <c>M</c>/<c>F</c> ↔ pavillon <c>Garcons</c>/<c>Filles</c> ; <c>Mixte</c> accepte tout.</summary>
    private async Task EnsureGenderAsync(Guid studentId, Bed bed, CancellationToken cancellationToken)
    {
        var dormitoryGender = await (
            from r in dbContext.DormitoryRooms
            join d in dbContext.Dormitories on r.DormitoryId equals d.Id
            where r.Id == bed.DormitoryRoomId
            select d.Gender).FirstAsync(cancellationToken);

        if (dormitoryGender == DormitoryGender.Mixte)
        {
            return;
        }

        // L'élève peut être NEUF et pas encore enregistré (inscription d'un nouvel élève, même transaction) : on regarde
        // d'abord les entités suivies par le contexte, puis la base.
        var studentGender = dbContext.Students.Local.FirstOrDefault(s => s.Id == studentId)?.Gender
            ?? await dbContext.Students.AsNoTracking()
                .Where(s => s.Id == studentId)
                .Select(s => s.Gender)
                .FirstOrDefaultAsync(cancellationToken);

        if (dormitoryGender == DormitoryGender.Garcons && studentGender != "M")
        {
            throw Invalid("BedId", "Ce pavillon accueille uniquement des garçons.");
        }

        if (dormitoryGender == DormitoryGender.Filles && studentGender != "F")
        {
            throw Invalid("BedId", "Ce pavillon accueille uniquement des filles.");
        }
    }

    private async Task<int> ResolveTuitionMonthsAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);
        var months = settings?.TuitionMonthsPerYear ?? SchoolSettingsDefaults.TuitionMonthsPerYear;

        return months < SchoolSettingsDefaults.MinTuitionMonths
            ? SchoolSettingsDefaults.TuitionMonthsPerYear
            : months;
    }

    // Dakar est en UTC+0 toute l'année, sans changement d'heure : la date UTC est la date locale de l'école.
    private DateOnly Today() => DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
