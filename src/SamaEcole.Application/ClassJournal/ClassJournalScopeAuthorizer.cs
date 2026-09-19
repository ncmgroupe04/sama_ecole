using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.ClassJournal;

/// <summary>
/// Garde d'écriture du cahier de texte (ticket JGK-P04) : réservée à l'Enseignant titulaire du
/// créneau, vérifiée contre <see cref="Domain.Entities.TeacherAssignment"/> (l'affectation classe/
/// matière de l'ANNÉE ACTIVE) ET <see cref="Domain.Entities.ScheduleSlot"/> (un créneau existe
/// bien ce jour de la semaine) — les deux ENSEMBLE, jamais l'un seul.
///
/// Pourquoi les deux : ScheduleSlot ne porte aucune SchoolYearId (c'est un créneau HEBDOMADAIRE
/// récurrent, jamais daté) — un contrôle ScheduleSlot seul matcherait donc aussi un créneau d'une
/// année scolaire révolue, encore présent en base. TeacherAssignment, lui, est borné à l'année
/// active mais ne dit rien du jour de la semaine. Même précédent de résolution
/// Teacher.UserId → Teacher.Id qu'AttendanceScopeAuthorizer/ScheduleOwnershipAuthorizer.
/// </summary>
public class ClassJournalScopeAuthorizer(
    IApplicationDbContext dbContext,
    ICurrentUserService currentUser)
{
    /// <summary>
    /// À l'écriture (création) uniquement : Directeur/Secrétariat/Surveillant/SuperAdmin restent
    /// non bornés côté lecture, mais SEUL l'Enseignant crée une entrée — voir
    /// ClassJournalController (rôle du endpoint POST). Renvoie l'id de la fiche enseignant, posé
    /// tel quel sur l'entité par le Handler.
    /// </summary>
    public async Task<Guid> EnsureCanJournalizeAsync(
        Guid classroomId, Guid subjectId, DateOnly sessionDate, Guid schoolYearId,
        CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant)
        {
            throw new UnauthorizedAccessException("Seul l'enseignant titulaire du créneau peut journaliser une séance.");
        }

        // Non-null garanti ici : GetOwnTeacherIdOrNullAsync ne renvoie null que pour un rôle non
        // borné, déjà écarté par le contrôle de rôle ci-dessus — pour Enseignant, elle lève plutôt
        // que de renvoyer null si aucune fiche n'est rattachée.
        var teacherId = (await GetOwnTeacherIdOrNullAsync(cancellationToken))!.Value;

        var isAssigned = await dbContext.TeacherAssignments.AnyAsync(
            a => a.TeacherId == teacherId
                 && a.ClassroomId == classroomId
                 && a.SubjectId == subjectId
                 && a.SchoolYearId == schoolYearId,
            cancellationToken);

        if (!isAssigned)
        {
            // 409, pas 403 : c'est l'ÉTAT de l'affectation qui bloque (le rôle, lui, est correct),
            // voir BusinessRuleException. Message actionnable plutôt qu'un refus muet
            // (docs/Volume_4_API_Design.md §22).
            throw new BusinessRuleException(
                "Vous n'êtes pas affecté à cette classe pour cette matière cette année : demandez au Directeur ou au Secrétariat de vous y affecter.",
                code: "SCHEDULE_SLOT_NOT_PLANNED");
        }

        var hasSlotThatDay = await dbContext.ScheduleSlots.AnyAsync(
            s => s.TeacherId == teacherId
                 && s.ClassroomId == classroomId
                 && s.SubjectId == subjectId
                 && s.DayOfWeek == sessionDate.DayOfWeek,
            cancellationToken);

        if (!hasSlotThatDay)
        {
            throw new BusinessRuleException(
                "Aucun créneau n'est planifié pour cette classe et cette matière ce jour-là : vérifiez la date, ou faites corriger votre emploi du temps.",
                code: "SCHEDULE_SLOT_NOT_PLANNED");
        }

        return teacherId;
    }

    /// <summary>
    /// Résout la fiche enseignant du compte courant, ou <c>null</c> pour un rôle NON BORNÉ
    /// (Directeur/Secrétariat — les seuls autres rôles à pouvoir modifier/supprimer une entrée,
    /// voir UpdateClassJournalEntryCommandHandler). <c>null</c> signifie donc toujours « ce rôle
    /// n'est pas restreint à ses propres entrées » : un Enseignant sans fiche rattachée lève au
    /// lieu de renvoyer <c>null</c>, pour ne jamais confondre les deux cas (même contrat que
    /// ScheduleOwnershipAuthorizer.GetOwnTeacherIdOrNullAsync).
    /// </summary>
    public async Task<Guid?> GetOwnTeacherIdOrNullAsync(CancellationToken cancellationToken)
    {
        if (currentUser.Role != Role.Enseignant) return null;

        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun compte associé à la session courante.");

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

        return teacherId;
    }
}
