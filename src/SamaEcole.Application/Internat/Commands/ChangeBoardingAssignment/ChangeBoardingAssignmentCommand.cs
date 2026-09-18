using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Enrollments;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.ChangeBoardingAssignment;

/// <summary>
/// POST /api/v1/internat/assignments — affecte, réaffecte ou libère un élève d'une chambre (spec
/// §5.2). RoomId null = libération. Réservé Directeur/Secretariat/Surveillant (InternatController).
///
/// <see cref="RowVersion"/> : verrou optimiste xmin d'Enrollment (AGENTS.md règle #5) — un dossier
/// modifié entre-temps (double clic, un autre utilisateur) est rejeté en 409, jamais écrasé.
/// </summary>
public record ChangeBoardingAssignmentCommand(
    Guid EnrollmentId, Guid? RoomId, BoardingStatus BoardingStatus, bool IncludeBoardingFee, uint RowVersion)
    : IRequest<EnrollmentBoardingDto>, IAuditableRequest;

public record EnrollmentBoardingDto(
    Guid EnrollmentId, BoardingStatus BoardingStatus, Guid? RoomId, decimal TotalDue, uint RowVersion);

public class ChangeBoardingAssignmentCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<ChangeBoardingAssignmentCommand, EnrollmentBoardingDto>
{
    public async Task<EnrollmentBoardingDto> Handle(
        ChangeBoardingAssignmentCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : une
        // inscription d'une autre école renvoie 404, jamais un changement silencieux.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(e => e.Id == request.EnrollmentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        // Cohérence RoomId / BoardingStatus (même garde que CreateEnrollmentCommandHandler, cf. revue
        // Task 4) : les deux champs ne doivent JAMAIS diverger. RoomId null n'est un état valide QUE
        // pour Externe (sémantique "null = libération") ; à l'inverse, un régime Interne/Demi-
        // pensionnaire exige toujours une chambre — sinon un élève "interne" resterait invisible de
        // toute occupation de chambre, sans jamais être compté nulle part.
        if (request.BoardingStatus == BoardingStatus.Externe && request.RoomId is not null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.RoomId), "Un élève Externe ne peut pas être affecté à une chambre.")
            ]);
        }

        if (request.BoardingStatus != BoardingStatus.Externe && request.RoomId is null)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.RoomId), "Une chambre est requise pour un régime Interne ou Demi-pensionnaire.")
            ]);
        }

        if (request.RoomId is { } roomId)
        {
            var room = await dbContext.Rooms.AsNoTracking()
                .FirstOrDefaultAsync(r => r.Id == roomId && r.Type == RoomType.Dortoir, cancellationToken)
                ?? throw new ValidationException([
                    new ValidationFailure(nameof(request.RoomId), "La chambre indiquée n'existe pas dans votre établissement.")
                ]);

            // Recompte l'occupation EN EXCLUANT l'inscription courante : confirmer une chambre déjà
            // occupée par CE MÊME élève (aucun changement réel) ne doit jamais être refusé pour cause
            // de capacité (spec §5.2).
            var occupied = await dbContext.Enrollments.CountAsync(
                e => e.RoomId == roomId && e.Id != request.EnrollmentId
                     && e.SchoolYearId == enrollment.SchoolYearId && e.Status != EnrollmentStatus.Cancelled,
                cancellationToken);

            if (occupied >= room.Capacity)
            {
                throw new ValidationException([
                    new ValidationFailure(nameof(request.RoomId), "Cette chambre a atteint sa capacité maximale.")
                ]);
            }
        }

        // Cœur du verrou optimiste (AGENTS.md règle #5) : positionné AVANT toute modification, pour
        // qu'un jeton périmé rejette l'écriture entière au SaveChangesAsync, jamais un état partiel.
        dbContext.SetOriginalConcurrencyToken(enrollment, request.RowVersion);

        // Aucune "libération" explicite de l'ancienne chambre : l'occupation est calculée par COMPTAGE
        // des inscriptions pointant vers une chambre (GetInternatDashboardQueryHandler), jamais par un
        // compteur stocké. Écraser RoomId ici suffit — l'ancienne chambre perd un occupant dès la
        // prochaine lecture, sans code de "libération" dédié (deuxième source de vérité à éviter).
        //
        // EnrollmentStatus (Confirmed/Cancelled/DroppedOut/Transferred) n'est ni lu ni modifié ici :
        // c'est un axe indépendant du régime d'hébergement, réservé à ChangeEnrollmentStatusCommand /
        // CancelEnrollmentCommand.
        enrollment.BoardingStatus = request.BoardingStatus;
        enrollment.RoomId = request.RoomId;

        // Pension : ajoutée seulement si absente (jamais de doublon sur un second transfert), jamais
        // retirée à la libération (spec §2, décision #7 — le dû annuel déjà facturé reste figé).
        if (request.IncludeBoardingFee && request.BoardingStatus != BoardingStatus.Externe)
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

        await dbContext.SaveChangesAsync(cancellationToken);

        // Jeton xmin RÉEL post-écriture (même technique que GetStudentDetailQueryHandler) : Task 16
        // (modale d'affectation) a besoin d'un jeton VALIDE pour permettre un second changement sans
        // faux 409 — jamais un placeholder à 0.
        var rowVersion = await dbContext.Enrollments
            .Where(e => e.Id == enrollment.Id)
            .Select(e => EF.Property<uint>(e, "xmin"))
            .SingleAsync(cancellationToken);

        return new EnrollmentBoardingDto(
            enrollment.Id, enrollment.BoardingStatus, enrollment.RoomId, enrollment.TotalDue, rowVersion);
    }

    private async Task<int> ResolveTuitionMonthsAsync(Guid schoolId, CancellationToken ct)
    {
        var settings = await dbContext.SchoolSettings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId, ct);
        var months = settings?.TuitionMonthsPerYear ?? SchoolSettingsDefaults.TuitionMonthsPerYear;

        return months < SchoolSettingsDefaults.MinTuitionMonths
            ? SchoolSettingsDefaults.TuitionMonthsPerYear
            : months;
    }
}
