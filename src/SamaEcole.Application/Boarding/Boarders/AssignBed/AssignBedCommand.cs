using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Boarding.Assignments;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Boarding.Boarders.AssignBed;

/// <summary>
/// POST /api/v1/boarding/assign-bed — affecte l'élève d'une inscription de l'année ACTIVE à un lit (régime
/// <c>Interne</c>) ou l'inscrit en demi-pension (aucun lit). S'il a déjà un séjour actif, c'est un TRANSFERT : le
/// <paramref name="RowVersion"/> du séjour est alors requis (409 s'il est périmé, règle #5). Un lit pris → 409
/// <c>BED_UNAVAILABLE</c> ; un lit en maintenance ou un pavillon du mauvais genre → 422.
/// <paramref name="IncludeBoardingFee"/> ajoute la ligne de pension une seule fois (jamais retirée ensuite).
/// </summary>
public record AssignBedCommand(Guid EnrollmentId, BoardingRegime Regime, Guid? BedId, bool IncludeBoardingFee, uint? RowVersion)
    : IRequest<BoarderListItemDto>, IAuditableRequest;

public class AssignBedCommandValidator : AbstractValidator<AssignBedCommand>
{
    public AssignBedCommandValidator()
    {
        RuleFor(x => x.EnrollmentId).NotEmpty();
        RuleFor(x => x.Regime).IsInEnum().WithMessage("Le régime d'hébergement indiqué n'est pas valide.");
        RuleFor(x => x.BedId).NotNull().When(x => x.Regime == BoardingRegime.Interne)
            .WithMessage("Un lit est requis pour un régime Interne.");
        RuleFor(x => x.BedId).Null().When(x => x.Regime == BoardingRegime.DemiPensionnaire)
            .WithMessage("Un demi-pensionnaire n'occupe pas de lit.");
    }
}

public class AssignBedCommandHandler(IApplicationDbContext dbContext, IBoardingAssignmentService assignments)
    : IRequestHandler<AssignBedCommand, BoarderListItemDto>
{
    public async Task<BoarderListItemDto> Handle(AssignBedCommand request, CancellationToken cancellationToken)
    {
        // L'inscription doit appartenir à l'année ACTIVE : sans cette borne, un identifiant périmé d'une année clôturée
        // serait accepté et on écrirait (voire facturerait) sur un dossier qui ne doit plus bouger.
        var activeYear = await dbContext.SchoolYears
            .FirstOrDefaultAsync(y => y.IsActive, cancellationToken)
            ?? throw new ValidationException([
                new ValidationFailure(
                    "SchoolYear", "Aucune année scolaire active. Activez une année scolaire avant de gérer l'internat.")
            ]);

        // Le Global Query Filter + la RLS bornent déjà la recherche à l'école courante : une inscription d'une autre
        // école, d'une année non active ou annulée renvoie 404, jamais un changement silencieux.
        var enrollment = await dbContext.Enrollments
            .FirstOrDefaultAsync(
                e => e.Id == request.EnrollmentId && e.SchoolYearId == activeYear.Id && e.Status != EnrollmentStatus.Cancelled,
                cancellationToken)
            ?? throw new KeyNotFoundException($"Inscription {request.EnrollmentId} introuvable.");

        var stay = await assignments.AssignAsync(
            enrollment, request.Regime, request.BedId, roomId: null, request.RowVersion,
            requireStayRowVersion: true, cancellationToken);

        if (request.IncludeBoardingFee)
        {
            await assignments.AddMissingBoardingFeeAsync(enrollment, cancellationToken);
        }

        await BoardingConflicts.SaveAsync(dbContext, cancellationToken);

        return await BoarderReader.GetItemAsync(dbContext, stay.Id, cancellationToken);
    }
}
