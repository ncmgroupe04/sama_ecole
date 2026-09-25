using FluentValidation.Results;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Attendance;

/// <summary>
/// Applique la règle de saisie de l'appel (ticket JGK-D06, feuille de route Présences) :
/// l'Enseignant ne peut faire l'appel QUE pour ses classes/matières assignées ; le Directeur et le
/// Secrétariat ne sont pas bornés.
///
/// La portée s'appuie sur le lien Teacher.UserId (ajouté en JGK-D06) : le JWT ne porte que
/// l'identifiant du COMPTE, on remonte donc à la FICHE enseignant, puis à ses TeacherAssignments de
/// l'année visée. Un enseignant sans fiche rattachée, ou sans attribution pour ce couple
/// classe/matière, se voit refuser l'accès en 403 — jamais un silence qui laisserait passer la saisie.
/// </summary>
public class AttendanceScopeAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// Résout le créneau d'un appel (Évolution N°5). SANS créneau demandé : le mode libre, inchangé — la période
    /// est celle que le client a saisie. AVEC un créneau : il doit exister dans l'école (422), convenir à la
    /// classe, la matière et le jour de la date (<see cref="SlotPeriod.Mismatch"/>, 422) et, pour un Enseignant,
    /// être LE SIEN (403) ; la période est alors DÉRIVÉE de ses horaires, jamais celle du client — l'index
    /// unique, les rapports et les notifications lisent tous <c>Period</c> et restent ainsi inchangés.
    /// </summary>
    public async Task<ResolvedPeriod> ResolveSlotAsync(
        Guid? scheduleSlotId, Guid classroomId, Guid subjectId, DateOnly date, string requestedPeriod,
        CancellationToken cancellationToken)
    {
        if (scheduleSlotId is not { } slotId)
        {
            return new ResolvedPeriod(null, requestedPeriod.Trim());
        }

        const string field = "ScheduleSlotId";

        var slot = await dbContext.ScheduleSlots.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == slotId, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(field, "Ce créneau n'existe pas dans votre établissement.")
            ]);

        var mismatch = SlotPeriod.Mismatch(slot, classroomId, subjectId, date);
        if (mismatch is not null)
        {
            throw new ValidationException([new ValidationFailure(field, mismatch)]);
        }

        await EnsureOwnsSlotAsync(slot, cancellationToken);

        return new ResolvedPeriod(slot.Id, SlotPeriod.Label(slot.StartTime, slot.EndTime));
    }

    /// <summary>
    /// Directeur, Secrétariat et Surveillant font l'appel de n'importe quel créneau — c'est le cas du
    /// REMPLAÇANT. Un Enseignant n'en fait que les siens : il doit être le titulaire du créneau (en plus d'être
    /// affecté à la classe et à la matière, contrôle fait par <see cref="EnsureCanTakeAttendanceAsync"/>).
    /// </summary>
    public async Task EnsureOwnsSlotAsync(Domain.Entities.ScheduleSlot slot, CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant)
        {
            return;
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        var teacherId = await dbContext.Teachers.AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId != slot.TeacherId)
        {
            throw new ForbiddenException(
                "Ce cours est assuré par un autre enseignant : demandez à la Vie Scolaire de faire l'appel à sa place.");
        }
    }

    public async Task EnsureCanTakeAttendanceAsync(
        Guid classroomId, Guid subjectId, Guid schoolYearId, CancellationToken cancellationToken)
    {
        // Le Directeur, le Secrétariat et le Surveillant gèrent l'appel de toutes les classes : aucune restriction.
        if (currentUser.Role is Role.Directeur or Role.Secretariat or Role.Surveillant)
        {
            return;
        }

        // Toute autre absence de rôle Enseignant est déjà écartée par [Authorize] au contrôleur ; ce
        // garde-fou couvre le cas où la méthode serait appelée hors de ce contexte.
        if (currentUser.Role != Role.Enseignant)
        {
            throw new UnauthorizedAccessException("Seuls l'Enseignant, le Directeur, le Secrétariat et le Surveillant peuvent faire l'appel.");
        }

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

        // Le compte Enseignant doit être rattaché à une FICHE (Teacher.UserId). Sans ce lien, on ne
        // peut pas savoir quelles classes lui sont assignées : refus.
        var teacherId = await dbContext.Teachers
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .Select(t => (Guid?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (teacherId is null)
        {
            throw new ForbiddenException(
                "Votre compte n'est rattaché à aucune fiche enseignant : demandez au Directeur de faire le rattachement.");
        }

        var isAssigned = await dbContext.TeacherAssignments.AnyAsync(
            a => a.TeacherId == teacherId
                 && a.ClassroomId == classroomId
                 && a.SubjectId == subjectId
                 && a.SchoolYearId == schoolYearId,
            cancellationToken);

        if (!isAssigned)
        {
            throw new ForbiddenException(
                "Vous n'êtes pas assigné à cette classe pour cette matière : vous ne pouvez pas en faire l'appel. "
                + "Demandez au Directeur ou au Secrétariat de vous y affecter, ou de faire l'appel à votre place.");
        }
    }
}

/// <summary>
/// Créneau résolu d'un appel (Évolution N°5) : le cours visé (null en mode libre) et la période à enregistrer —
/// dérivée des horaires du cours, ou celle que le client a saisie en mode libre.
/// </summary>
public sealed record ResolvedPeriod(Guid? SlotId, string Period);
