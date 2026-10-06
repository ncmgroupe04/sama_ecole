using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Enums;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.UpdateInstructor;

/// <summary>
/// PUT /api/v1/internat/instructors/{id} — modifie la fiche d'un Oustaz : identité, statut (suspension), compte lié.
/// Réservé au Directeur (InternatController). Le client renvoie la fiche ENTIÈRE, jamais un correctif partiel (même
/// convention que les paramètres d'école) : <see cref="UserId"/> null DÉTACHE le compte.
///
/// Suspendre (ou bloquer) un Oustaz coupe aussitôt son accès à sa Halqa (HalqaScopeAuthorizer relit le statut à chaque
/// requête) et lui interdit toute nouvelle affectation. Ses élèves restent rattachés tant que la Direction ne les a
/// pas réaffectés : rien n'est détaché en silence.
///
/// <see cref="RowVersion"/> : jeton xmin lu avec la fiche (AGENTS.md règle #5) — périmé, l'écriture est refusée en 409.
/// </summary>
public record UpdateInstructorCommand(
    Guid Id, string FullName, string? FullNameAr, string? Phone, Guid? UserId, EntityStatus Status, uint RowVersion)
    : IRequest<InstructorDto>, IAuditableRequest;

public class UpdateInstructorCommandValidator : AbstractValidator<UpdateInstructorCommand>
{
    public UpdateInstructorCommandValidator()
    {
        RuleFor(c => c.Id).NotEmpty();
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(c => c.FullNameAr).MaximumLength(200).NoHtml();
        RuleFor(c => c.Phone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(c => c.UserId).NotEqual(Guid.Empty).When(c => c.UserId is not null);
        RuleFor(c => c.Status).IsInEnum().WithMessage("Statut invalide.");
    }
}

public class UpdateInstructorCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<UpdateInstructorCommand, InstructorDto>
{
    public async Task<InstructorDto> Handle(UpdateInstructorCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        // Global Query Filter + RLS : l'Oustaz d'une autre école est structurellement introuvable (404).
        var instructor = await dbContext.Instructors
            .FirstOrDefaultAsync(i => i.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Oustaz {request.Id} introuvable.");

        // Cœur du verrou optimiste : jeton périmé → SaveChangesAsync refuse en 409.
        dbContext.SetOriginalConcurrencyToken(instructor, request.RowVersion);

        // Le compte n'est revalidé QUE s'il change : un Oustaz dont le compte a changé de rôle depuis doit pouvoir
        // être renommé ou suspendu sans que l'ancien rattachement ne bloque la fiche.
        if (request.UserId is { } userId && userId != instructor.UserId)
        {
            await InstructorUserAccountLink.EnsureLinkableAsync(
                dbContext, schoolId, userId, excludeInstructorId: instructor.Id, cancellationToken);
        }

        instructor.FullName = request.FullName.ToTitleCase();
        instructor.FullNameAr = Trimmed(request.FullNameAr);
        instructor.Phone = Trimmed(request.Phone);
        instructor.UserId = request.UserId;
        instructor.Status = request.Status;

        await dbContext.SaveChangesAsync(cancellationToken);

        return await InstructorProjection.Dtos(dbContext, instructor.Id).FirstAsync(cancellationToken);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
