using SamaEcole.Application.Common.Extensions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Internat.Commands.CreateInstructor;

/// <summary>
/// POST /api/v1/internat/instructors — crée la fiche d'un Oustaz, avec en option le compte de connexion qui lui
/// permettra d'ouvrir sa Halqa sur la tablette. Réservé au Directeur (InternatController). Un Oustaz naît toujours
/// <c>Active</c> : on ne crée pas une fiche déjà suspendue.
/// </summary>
public record CreateInstructorCommand(string FullName, string? FullNameAr, string? Phone, Guid? UserId)
    : IRequest<InstructorDto>, IAuditableRequest;

public class CreateInstructorCommandValidator : AbstractValidator<CreateInstructorCommand>
{
    public CreateInstructorCommandValidator()
    {
        RuleFor(c => c.FullName).NotEmpty().MaximumLength(200).NoHtml();
        RuleFor(c => c.FullNameAr).MaximumLength(200).NoHtml();
        RuleFor(c => c.Phone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        RuleFor(c => c.UserId).NotEqual(Guid.Empty).When(c => c.UserId is not null);
    }
}

public class CreateInstructorCommandHandler(IApplicationDbContext dbContext, ITenantProvider tenantProvider)
    : IRequestHandler<CreateInstructorCommand, InstructorDto>
{
    public async Task<InstructorDto> Handle(CreateInstructorCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (request.UserId is { } userId)
        {
            await InstructorUserAccountLink.EnsureLinkableAsync(
                dbContext, schoolId, userId, excludeInstructorId: null, cancellationToken);
        }

        var instructor = new Instructor
        {
            SchoolId = schoolId,
            FullName = request.FullName.ToTitleCase(),
            FullNameAr = Trimmed(request.FullNameAr),
            Phone = Trimmed(request.Phone),
            UserId = request.UserId
        };

        dbContext.Instructors.Add(instructor);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await InstructorProjection.Dtos(dbContext, instructor.Id).FirstAsync(cancellationToken);
    }

    private static string? Trimmed(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
