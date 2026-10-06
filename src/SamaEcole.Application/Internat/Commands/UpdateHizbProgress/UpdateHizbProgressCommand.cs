using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.UpdateHizbProgress;

/// <summary>
/// PUT /api/v1/internat/students/{studentId}/hizb-progress — enregistre l'avancement d'UN Hizb d'un élève (quarts
/// acquis, qualité de récitation). Crée la ligne si ce Hizb n'a jamais été saisi, la met à jour sinon : un seul
/// état courant par (élève, Hizb).
///
/// Le client n'envoie NI l'état NI la date : l'état se déduit des quarts (<see cref="HizbRules.StateFor"/>) et
/// la date d'évaluation est celle du serveur — un Oustaz ne peut pas antidater ni contredire ses propres quarts.
///
/// <see cref="RowVersion"/> : jeton xmin lu avec la grille (AGENTS.md règle #5). <c>null</c> pour un Hizb jamais
/// saisi ; obligatoire dès que la ligne existe — sinon 409, jamais un écrasement silencieux.
/// </summary>
public record UpdateHizbProgressCommand(
    Guid StudentId, int HizbNumber, int CompletedQuarters, int? Rating, uint? RowVersion)
    : IRequest<HizbCellDto>;

public class UpdateHizbProgressCommandValidator : AbstractValidator<UpdateHizbProgressCommand>
{
    public UpdateHizbProgressCommandValidator()
    {
        RuleFor(c => c.StudentId).NotEmpty();
        RuleFor(c => c.HizbNumber).InclusiveBetween(1, HizbRules.HizbCount)
            .WithMessage($"Le numéro de Hizb doit être compris entre 1 et {HizbRules.HizbCount}.");
        RuleFor(c => c.CompletedQuarters).InclusiveBetween(0, HizbRules.QuartersPerHizb)
            .WithMessage($"Le nombre de quarts doit être compris entre 0 et {HizbRules.QuartersPerHizb}.");
        RuleFor(c => c.Rating).InclusiveBetween(HizbRules.MinRating, HizbRules.MaxRating)
            .When(c => c.Rating is not null)
            .WithMessage($"La note doit être comprise entre {HizbRules.MinRating} et {HizbRules.MaxRating}.");

        // Un Hizb non commencé n'a pas de note : on n'en invente pas une.
        RuleFor(c => c.Rating).Null()
            .When(c => c.CompletedQuarters == 0)
            .WithMessage("Un Hizb non commencé ne peut pas avoir de note.");
    }
}

public class UpdateHizbProgressCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    HalqaScopeAuthorizer scopeAuthorizer,
    TimeProvider timeProvider)
    : IRequestHandler<UpdateHizbProgressCommand, HizbCellDto>
{
    public async Task<HizbCellDto> Handle(UpdateHizbProgressCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Global Query Filter + RLS : l'élève d'une autre école est structurellement introuvable (404).
        var student = await dbContext.Students
            .AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => new { s.Id, s.InstructorId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Élève {request.StudentId} introuvable.");

        // L'Oustaz n'écrit que pour SA Halqa ; le Directeur écrit pour tous (403 sinon).
        await scopeAuthorizer.EnsureCanAccessStudentAsync(student.InstructorId, cancellationToken);

        var evaluated = request.CompletedQuarters > 0;

        var status = await dbContext.StudentHizbStatuses
            .FirstOrDefaultAsync(h => h.StudentId == request.StudentId && h.HizbNumber == request.HizbNumber, cancellationToken);

        if (status is null)
        {
            // Le client croyait la ligne existante (jeton fourni) alors qu'elle a disparu : c'est un conflit, pas une création.
            if (request.RowVersion is not null)
            {
                throw new ConcurrencyConflictException(nameof(StudentHizbStatus), $"{request.StudentId}/{request.HizbNumber}");
            }

            status = new StudentHizbStatus
            {
                SchoolId = schoolId,
                StudentId = request.StudentId,
                HizbNumber = request.HizbNumber
            };
            dbContext.StudentHizbStatuses.Add(status);
        }
        else
        {
            // Ligne existante sans jeton : le client n'a pas vu la version courante — on refuse plutôt que d'écraser.
            if (request.RowVersion is null)
            {
                throw new ConcurrencyConflictException(nameof(StudentHizbStatus), status.Id);
            }

            // Cœur du verrou optimiste : jeton périmé → SaveChangesAsync refuse en 409.
            dbContext.SetOriginalConcurrencyToken(status, request.RowVersion.Value);
        }

        status.CompletedQuarters = request.CompletedQuarters;
        status.State = HizbRules.StateFor(request.CompletedQuarters);
        status.Rating = evaluated ? request.Rating : null;
        status.LastEvaluatedAt = evaluated ? timeProvider.GetUtcNow() : null;

        await dbContext.SaveChangesAsync(cancellationToken);

        var rowVersion = await dbContext.StudentHizbStatuses.AsNoTracking()
            .Where(h => h.Id == status.Id)
            .Select(h => EF.Property<uint>(h, "xmin"))
            .FirstAsync(cancellationToken);

        return new HizbCellDto(
            status.HizbNumber, status.CompletedQuarters, status.State, status.LastEvaluatedAt, status.Rating, rowVersion);
    }
}
