using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Common.Validation;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.RollBook.Commands.UpsertRollBookEntries;

/// <summary>
/// Une case du cahier. <c>Status</c> nul = « revenir au statut calculé » (la saisie existante est supprimée
/// logiquement, la case retrouve ce que disent les appels par séance).
/// </summary>
public record RollBookEntryInput(
    Guid StudentId,
    DateOnly Date,
    HalfDay HalfDay,
    AttendanceStatus? Status,
    int LateMinutes = 0,
    string? Reason = null);

/// <summary>
/// PUT /api/v1/roll-book/entries — enregistre en UN lot, atomique, des cases d'une classe (une transaction :
/// tout passe ou rien). Le SchoolId vient du JWT (règle #10).
/// </summary>
public record UpsertRollBookEntriesCommand(Guid ClassroomId, IReadOnlyList<RollBookEntryInput> Entries)
    : IRequest<UpsertRollBookEntriesResult>;

public class UpsertRollBookEntriesCommandValidator : AbstractValidator<UpsertRollBookEntriesCommand>
{
    public const int MaxEntries = 2000;

    public UpsertRollBookEntriesCommandValidator()
    {
        RuleFor(c => c.ClassroomId).NotEmpty().WithMessage("La classe est obligatoire.");

        RuleFor(c => c.Entries)
            .NotEmpty().WithMessage("Aucune case à enregistrer.")
            .Must(e => e.Count <= MaxEntries).WithMessage($"Un lot ne peut pas dépasser {MaxEntries} cases.")
            .Must(e => e.Select(x => (x.StudentId, x.Date, x.HalfDay)).Distinct().Count() == e.Count)
            .WithMessage("Une même case (élève, jour, demi-journée) figure plusieurs fois dans le lot.");

        RuleForEach(c => c.Entries).ChildRules(entry =>
        {
            entry.RuleFor(e => e.StudentId).NotEmpty().WithMessage("L'élève est obligatoire.");
            entry.RuleFor(e => e.Date).NotEqual(default(DateOnly)).WithMessage("La date est obligatoire.");
            entry.RuleFor(e => e.HalfDay).IsInEnum().WithMessage("Demi-journée inconnue.");
            entry.RuleFor(e => e.Status).IsInEnum().When(e => e.Status.HasValue).WithMessage("Statut inconnu.");
            entry.RuleFor(e => e.LateMinutes).InclusiveBetween(0, 600).WithMessage("Les minutes de retard vont de 0 à 600.");
            entry.RuleFor(e => e.LateMinutes).Equal(0)
                .When(e => e.Status != AttendanceStatus.Late)
                .WithMessage("Des minutes de retard n'ont de sens que pour le statut « Retard ».");
            entry.RuleFor(e => e.Reason).MaximumLength(300).WithMessage("Le motif ne peut pas dépasser 300 caractères.").NoHtml();
        });
    }
}

public class UpsertRollBookEntriesCommandHandler(
    IApplicationDbContext dbContext,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    RollBookScopeAuthorizer scope,
    TimeProvider timeProvider,
    ILogger<UpsertRollBookEntriesCommandHandler> logger)
    : IRequestHandler<UpsertRollBookEntriesCommand, UpsertRollBookEntriesResult>
{
    public async Task<UpsertRollBookEntriesResult> Handle(
        UpsertRollBookEntriesCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var actorId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Utilisateur non identifié.");

        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.ClassroomId), "La classe indiquée n'existe pas dans votre établissement.")
            ]);
        }

        var failures = new List<ValidationFailure>();

        // Année de chaque date : une case appartient à l'année qui couvre son jour (l'active en cas de doute).
        var years = (await dbContext.SchoolYears.AsNoTracking().ToListAsync(cancellationToken))
            .OrderByDescending(y => y.IsActive).ToList();
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        var yearByDate = new Dictionary<DateOnly, SchoolYear>();
        foreach (var date in request.Entries.Select(e => e.Date).Distinct())
        {
            var year = years.FirstOrDefault(y => date >= y.StartDate && date <= y.EndDate);
            if (year is null)
            {
                failures.Add(new ValidationFailure("Entries", $"Le {date:dd/MM/yyyy} ne tombe dans aucune année scolaire."));
            }
            else if (date > today)
            {
                failures.Add(new ValidationFailure("Entries", $"Le {date:dd/MM/yyyy} est dans le futur : le cahier ne se remplit pas à l'avance."));
            }
            else
            {
                yearByDate[date] = year;
            }
        }

        // Les élèves visés doivent appartenir à la classe : on ne remplit pas le cahier d'une autre classe.
        var studentIds = request.Entries.Select(e => e.StudentId).Distinct().ToList();
        var rosterIds = (await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId && studentIds.Contains(s.Id))
            .Select(s => s.Id)
            .ToListAsync(cancellationToken)).ToHashSet();

        foreach (var unknown in studentIds.Where(id => !rosterIds.Contains(id)))
        {
            failures.Add(new ValidationFailure("Entries", $"L'élève {unknown} n'appartient pas à cette classe."));
        }

        if (failures.Count > 0)
        {
            throw new ValidationException(failures);
        }

        foreach (var year in yearByDate.Values.DistinctBy(y => y.Id))
        {
            await scope.EnsureCanAccessClassroomAsync(request.ClassroomId, year.Id, cancellationToken);
        }

        var minDate = request.Entries.Min(e => e.Date);
        var maxDate = request.Entries.Max(e => e.Date);
        var existing = (await dbContext.RollBookEntries
            .Where(e => e.ClassroomId == request.ClassroomId && e.Date >= minDate && e.Date <= maxDate && studentIds.Contains(e.StudentId))
            .ToListAsync(cancellationToken))
            .ToDictionary(e => (e.StudentId, e.Date, e.HalfDay));

        int created = 0, updated = 0, reverted = 0;

        foreach (var input in request.Entries)
        {
            existing.TryGetValue((input.StudentId, input.Date, input.HalfDay), out var entry);

            if (input.Status is not { } status)
            {
                if (entry is not null)
                {
                    entry.SoftDelete(actorId.ToString());
                    reverted++;
                }

                continue;
            }

            var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();

            if (entry is null)
            {
                dbContext.RollBookEntries.Add(new RollBookEntry
                {
                    SchoolId = schoolId,
                    SchoolYearId = yearByDate[input.Date].Id,
                    ClassroomId = request.ClassroomId,
                    StudentId = input.StudentId,
                    Date = input.Date,
                    HalfDay = input.HalfDay,
                    Status = status,
                    LateMinutes = input.LateMinutes,
                    Reason = reason,
                    RecordedByUserId = actorId
                });
                created++;
            }
            else
            {
                entry.Status = status;
                entry.LateMinutes = input.LateMinutes;
                entry.Reason = reason;
                entry.RecordedByUserId = actorId;
                updated++;
            }
        }

        // Un seul SaveChanges : le lot est atomique. Deux enregistrements concurrents de la même case butent sur
        // l'index unique partiel et remontent en 409, jamais en écrasement silencieux.
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Cahier de roulement de la classe {ClassroomId} (école {SchoolId}) : {Created} créée(s), {Updated} modifiée(s), {Reverted} remise(s) au calcul.",
            request.ClassroomId, schoolId, created, updated, reverted);

        return new UpsertRollBookEntriesResult(created, updated, reverted);
    }
}
