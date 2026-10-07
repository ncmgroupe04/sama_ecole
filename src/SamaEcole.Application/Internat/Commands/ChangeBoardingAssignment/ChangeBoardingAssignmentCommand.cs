using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;

/// <summary>
/// POST /api/v1/internat/assignments — affecte, réaffecte ou libère un élève d'une chambre (spec du 18/09, §5.2).
/// RoomId null = libération. Réservé Directeur/Secretariat/Surveillant (InternatController).
///
/// CONTRAT HÉRITÉ, CONSERVÉ À L'IDENTIQUE pour l'ancien écran <c>/internat</c> (lot C de la spec Pavillon/Lit) : mêmes
/// entrées, même <see cref="EnrollmentBoardingDto"/>. Ce handler n'est plus qu'un ADAPTATEUR de
/// <see cref="IBoardingAssignmentService"/> — la règle d'affectation n'existe qu'à un endroit. À supprimer au lot F, avec
/// l'ancien écran.
///
/// <see cref="RowVersion"/> : verrou optimiste xmin de l'INSCRIPTION (AGENTS.md règle #5), jeton historique de l'ancien écran
/// (il le lit dans <c>SearchBoardableStudentsQuery</c>). L'affectation n'écrit plus de colonne de l'inscription ; pour que ce
/// jeton continue de détecter « dossier modifié par un autre utilisateur », l'inscription est « touchée » (UpdatedAt) à chaque
/// appel, ce qui fait avancer son xmin. RoomId désigne désormais une <c>DormitoryRoom</c>.
/// </summary>
public record ChangeBoardingAssignmentCommand(
    Guid EnrollmentId, Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion)
    : IRequest<EnrollmentBoardingDto>, IAuditableRequest;

public record EnrollmentBoardingDto(
    Guid EnrollmentId, BoardingStatus BoardingStatus, Guid? RoomId, decimal TotalDue, uint RowVersion);

public class ChangeBoardingAssignmentCommandHandler(
    IApplicationDbContext dbContext, IBoardingAssignmentService assignments, TimeProvider timeProvider)
    : IRequestHandler<ChangeBoardingAssignmentCommand, EnrollmentBoardingDto>
{
    public async Task<EnrollmentBoardingDto> Handle(
        ChangeBoardingAssignmentCommand request, CancellationToken cancellationToken)
    {
        // Garde de forme AVANT toute lecture : Program.cs enregistre JsonStringEnumConverter() sans
        // allowIntegerValues: false, donc un client peut poster une valeur d'enum hors domaine ({"boardingStatus": 99}).
        if (!Enum.IsDefined(request.BoardingStatus))
        {
            throw Invalid(nameof(request.BoardingStatus), "Le régime d'hébergement indiqué n'est pas valide.");
        }

        // L'inscription doit être rattachée à l'année ACTIVE : sans cette borne, un identifiant périmé d'une année clôturée
        // serait accepté et on écrirait (voire facturerait) sur un dossier qui ne doit plus bouger.
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw Invalid(
                "SchoolYear", "Aucune année scolaire active. Activez une année scolaire avant de gérer l'internat.");

        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : une inscription d'une autre école,
        // ou d'une année non active, renvoie 404, jamais un changement silencieux.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId && e.SchoolYearId == activeYear.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        // Cohérence régime / chambre, comme avant : « null = libération » n'est valide que pour Externe ; un Interne exige une
        // chambre. Le DemiPensionnaire, lui, n'en exige plus (il ne dort pas à l'internat) et ignore celle qu'on lui envoie.
        if (request.BoardingStatus == BoardingStatus.Externe && request.RoomId is not null)
        {
            throw Invalid(nameof(request.RoomId), "Un élève Externe ne peut pas être affecté à une chambre.");
        }

        if (request.BoardingStatus == BoardingStatus.Interne && request.RoomId is null)
        {
            throw Invalid(nameof(request.RoomId), "Une chambre est requise pour un régime Interne.");
        }

        // Cœur du verrou optimiste : posé AVANT toute modification, pour qu'un jeton périmé rejette l'écriture entière au
        // SaveChanges, jamais un état partiel. L'inscription est ensuite « touchée » pour que son xmin avance même quand
        // seule la table des séjours change.
        dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);
        enrollment.UpdatedAt = timeProvider.GetUtcNow();

        BoardingEnrollment? stay;
        if (request.BoardingStatus == BoardingStatus.Externe)
        {
            stay = await assignments.EndAsync(enrollment.Id, cancellationToken);
        }
        else
        {
            var regime = request.BoardingStatus == BoardingStatus.Interne
                ? BoardingRegime.Interne
                : BoardingRegime.DemiPensionnaire;

            stay = await assignments.AssignAsync(
                enrollment, regime, bedId: null, request.RoomId, stayRowVersion: null,
                requireStayRowVersion: false, cancellationToken);
        }

        // Pension : ajoutée seulement si absente (jamais de doublon sur un second transfert), jamais retirée à la libération
        // (décision #7 de la spec du 18/09 — le dû annuel déjà facturé reste figé).
        if (request.IncludeBoardingFee && request.BoardingStatus != BoardingStatus.Externe)
        {
            await assignments.AddMissingBoardingFeeAsync(enrollment, cancellationToken);
        }

        await BoardingConflicts.SaveAsync(dbContext, cancellationToken);

        // Jeton xmin RÉEL post-écriture : la modale d'affectation a besoin d'un jeton VALIDE pour un second changement sans
        // faux 409 — jamais un placeholder à 0.
        var rowVersion = await dbContext.Enrollments
            .Where(e => e.Id == enrollment.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .SingleAsync(cancellationToken);

        var roomId = stay is { IsActive: true, BedId: { } bedId }
            ? await dbContext.Beds.AsNoTracking()
                .Where(b => b.Id == bedId)
                .Select(b => (Guid?)b.DormitoryRoomId)
                .FirstOrDefaultAsync(cancellationToken)
            : null;

        var status = stay is { IsActive: true }
            ? (stay.Regime == BoardingRegime.Interne ? BoardingStatus.Interne : BoardingStatus.DemiPensionnaire)
            : BoardingStatus.Externe;

        return new EnrollmentBoardingDto(enrollment.Id, status, roomId, enrollment.TotalDue, rowVersion);
    }

    private static ValidationException Invalid(string field, string message) =>
        new([new ValidationFailure(field, message)]);
}
