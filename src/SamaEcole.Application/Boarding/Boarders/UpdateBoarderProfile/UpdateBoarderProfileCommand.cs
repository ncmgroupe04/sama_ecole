using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Boarders.UpdateBoarderProfile;

/// <summary>
/// PUT /api/v1/boarding/boarders/{id}/profile — fiche médicale, contact d'urgence et personnes habilitées à récupérer
/// l'élève (dix au plus). <paramref name="MedicalNotes"/> est une donnée de santé : seuls le Directeur et le Surveillant
/// l'écrivent (le Secrétariat reçoit 403 s'il en envoie une valeur, et la fiche existante est alors conservée). Cette
/// commande n'est volontairement PAS <c>IAuditableRequest</c> : en cas d'échec le journal enregistre l'exception, et aucune
/// exception de ce fichier ne doit contenir une note médicale.
/// </summary>
public record UpdateBoarderProfileCommand(
    Guid Id,
    string? MedicalNotes,
    string? EmergencyContactName,
    string? EmergencyContactPhone,
    IReadOnlyList<AllowedExitPersonDto> AllowedExitPersons,
    uint RowVersion) : IRequest<BoarderDetailDto>;

public class UpdateBoarderProfileCommandValidator : AbstractValidator<UpdateBoarderProfileCommand>
{
    public const int MaxAllowedExitPersons = 10;

    public UpdateBoarderProfileCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.MedicalNotes).MaximumLength(2000).NoHtml();
        RuleFor(x => x.EmergencyContactName).MaximumLength(150).NoHtml();
        RuleFor(x => x.EmergencyContactPhone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();

        RuleFor(x => x.AllowedExitPersons)
            .NotNull()
            .Must(persons => persons.Count <= MaxAllowedExitPersons)
            .WithMessage($"Dix personnes habilitées au plus.");

        RuleForEach(x => x.AllowedExitPersons).ChildRules(person =>
        {
            person.RuleFor(p => p.Name).NotEmpty().MaximumLength(150).NoHtml();
            person.RuleFor(p => p.Relationship).MaximumLength(50).NoHtml();
            person.RuleFor(p => p.Phone).MaximumLength(30).NoHtml().MustBeValidSenegalPhone();
        });
    }
}

public class UpdateBoarderProfileCommandHandler(IApplicationDbContext dbContext, ICurrentUserService currentUser)
    : IRequestHandler<UpdateBoarderProfileCommand, BoarderDetailDto>
{
    public async Task<BoarderDetailDto> Handle(UpdateBoarderProfileCommand request, CancellationToken cancellationToken)
    {
        var role = currentUser.Role;
        var canWriteMedical = BoarderDetailReader.CanReadMedical(role);

        // Le message ne cite JAMAIS la valeur envoyée : l'audit journalise ex.ToString() en cas d'échec.
        if (!canWriteMedical && request.MedicalNotes is not null)
        {
            throw new ForbiddenException("Seuls le Directeur et le Surveillant peuvent renseigner la fiche médicale.");
        }

        var stay = await dbContext.BoardingEnrollments
            .FirstOrDefaultAsync(b => b.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Séjour {request.Id} introuvable.");

        dbContext.SetOriginalConcurrencyToken(stay, request.RowVersion);

        stay.EmergencyContactName = NullIfBlank(request.EmergencyContactName);
        stay.EmergencyContactPhone = NullIfBlank(request.EmergencyContactPhone);

        // Pour le Directeur et le Surveillant, la fiche est REMPLACÉE (vide = effacée). Pour les autres rôles, elle n'est
        // pas touchée : le champ est absent de la requête.
        if (canWriteMedical)
        {
            stay.MedicalNotes = NullIfBlank(request.MedicalNotes);
        }

        stay.AllowedExitPersons = request.AllowedExitPersons
            .Select(p => new AllowedExitPerson { Name = p.Name.Trim(), Relationship = p.Relationship.Trim(), Phone = p.Phone.Trim() })
            .ToList();

        await dbContext.SaveChangesAsync(cancellationToken);

        return await BoarderDetailReader.GetAsync(dbContext, stay.Id, role, cancellationToken);
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
