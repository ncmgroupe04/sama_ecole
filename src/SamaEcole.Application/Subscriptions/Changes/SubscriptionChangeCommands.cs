using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Registration;
using SamaEcole.Application.Schools;
using SamaEcole.Application.Schools.Commands.ApplyEstablishmentProfile;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using ValidationException = SamaEcole.Application.Common.Exceptions.ValidationException;

namespace SamaEcole.Application.Subscriptions.Changes;

// ═══════════════════════════════════════════════════════════════════════ Côté Directeur

/// <summary>
/// POST /tenant-subscription/change-requests/preview — ce que coûterait et changerait cette modification, SANS rien écrire.
/// Sert au bandeau de quota (« passer à la tranche supérieure ») et à tout écran qui veut annoncer le tarif ajusté avant
/// d'envoyer la demande. Aucun SchoolId : l'école vient du JWT (AGENTS.md règle #10).
/// </summary>
public record PreviewSubscriptionChangeQuery(SubscriptionChangeSet Change) : IRequest<SubscriptionChangePreview>;

public class PreviewSubscriptionChangeQueryHandler(SubscriptionChangeService service)
    : IRequestHandler<PreviewSubscriptionChangeQuery, SubscriptionChangePreview>
{
    public async Task<SubscriptionChangePreview> Handle(PreviewSubscriptionChangeQuery request, CancellationToken cancellationToken)
    {
        var evaluation = await service.EvaluateAsync(request.Change, cancellationToken)
            ?? throw new BusinessRuleException(
                "Votre abonnement n'est pas encore configuré : terminez la configuration de votre établissement.",
                "ONBOARDING_NOT_COMPLETED");

        return evaluation.Preview;
    }
}

/// <summary>
/// POST /tenant-subscription/change-requests — le Directeur envoie la demande que la modale lui a proposée. Elle ne modifie
/// RIEN : le Super Admin la valide (ou la rejette) une fois le règlement convenu, et seulement alors l'abonnement et les
/// réglages de l'école sont écrits. Une seule demande en attente par école.
///
/// IsAuditableRequest : un changement d'abonnement laisse une trace dans le journal d'audit.
/// </summary>
public record CreateSubscriptionChangeRequestCommand(SubscriptionChangeSet Change, string? Note)
    : IRequest<SubscriptionChangeRequestDto>, IAuditableRequest;

public class CreateSubscriptionChangeRequestCommandValidator : AbstractValidator<CreateSubscriptionChangeRequestCommand>
{
    public CreateSubscriptionChangeRequestCommandValidator()
    {
        RuleFor(c => c.Change).NotNull();

        RuleFor(c => c.Change.Profile).IsInEnum().When(c => c.Change?.Profile is not null);
        RuleFor(c => c.Change.Tier).IsInEnum().When(c => c.Change?.Tier is not null);

        RuleFor(c => c.Change.Cycles)
            .Must(cycles => cycles!.All(Enum.IsDefined) && cycles!.Distinct().Count() == cycles!.Count)
            .WithMessage("Les cycles demandés sont invalides.")
            .When(c => c.Change?.Cycles is not null);

        RuleFor(c => c.Note).MaximumLength(500).When(c => c.Note is not null);
    }
}

public class CreateSubscriptionChangeRequestCommandHandler(
    IApplicationDbContext dbContext,
    SubscriptionChangeService service,
    ITenantProvider tenantProvider,
    ICurrentUserService currentUser,
    ILogger<CreateSubscriptionChangeRequestCommandHandler> logger)
    : IRequestHandler<CreateSubscriptionChangeRequestCommand, SubscriptionChangeRequestDto>
{
    public async Task<SubscriptionChangeRequestDto> Handle(
        CreateSubscriptionChangeRequestCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");
        var userId = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var evaluation = await service.EvaluateAsync(request.Change, cancellationToken)
            ?? throw new BusinessRuleException(
                "Votre abonnement n'est pas encore configuré : terminez la configuration de votre établissement.",
                "ONBOARDING_NOT_COMPLETED");

        if (evaluation.Plan.IsNoOp)
        {
            throw new ValidationException([
                new ValidationFailure(nameof(request.Change), "Cette demande ne change rien à votre abonnement actuel.")
            ]);
        }

        if (evaluation.Plan.Problems.Count > 0)
        {
            throw new ValidationException(evaluation.Plan.Problems
                .Select(p => new ValidationFailure(nameof(request.Change), p.Message)));
        }

        if (evaluation.Preview.HasPendingRequest)
        {
            throw new BusinessRuleException(
                "Une demande de changement est déjà en cours d'examen pour votre établissement. Attendez la réponse de notre équipe, ou retirez-la.",
                "CHANGE_REQUEST_PENDING");
        }

        // Nom et adresse figés maintenant : le Super Admin, qui décidera, ne peut pas lire les comptes (RLS) et c'est à cette
        // adresse que part l'e-mail de décision. Le Global Query Filter borne la lecture à l'école du JWT.
        var requester = await dbContext.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.FullName, u.Email })
            .FirstOrDefaultAsync(cancellationToken);

        var current = evaluation.Current;
        var target = evaluation.Plan.Target;
        var (max, soft) = target.Tier == StudentQuotaTier.Tier4_Custom
            ? (evaluation.Subscription.MaxStudentLimit, evaluation.Subscription.SoftQuotaLimit)
            : StudentQuotaDefaults.For(target.Tier);

        var entity = new SubscriptionChangeRequest
        {
            SchoolId = schoolId,
            Status = SubscriptionChangeStatus.Pending,
            IsDowngrade = evaluation.Plan.IsDowngrade,

            CurrentProfile = current.Profile,
            CurrentTier = current.Tier,
            CurrentCycles = ManagedCycleSet.Serialize(current.Cycles),
            CurrentHasBoarding = current.HasBoarding,
            CurrentIsPedagogyEnabled = current.Modules.IsPedagogyEnabled,
            CurrentIsFinanceEnabled = current.Modules.IsFinanceEnabled,
            CurrentIsInternatEnabled = current.Modules.IsInternatEnabled,
            CurrentIsCoranModuleEnabled = current.Modules.IsCoranModuleEnabled,

            TargetProfile = target.Profile,
            TargetEstablishmentProfile = Subscriptions.ProfileTypeMapping.ToEstablishmentProfile(target.Profile),
            TargetTier = target.Tier,
            TargetMaxStudentLimit = max,
            TargetSoftQuotaLimit = soft,
            TargetCycles = ManagedCycleSet.Serialize(target.Cycles),
            TargetHasBoarding = target.HasBoarding,
            TargetIsPedagogyEnabled = target.Modules.IsPedagogyEnabled,
            TargetIsFinanceEnabled = target.Modules.IsFinanceEnabled,
            TargetIsInternatEnabled = target.Modules.IsInternatEnabled,
            TargetIsCoranModuleEnabled = target.Modules.IsCoranModuleEnabled,

            CurrentMonthlyXof = evaluation.Preview.Current.MonthlyXof,
            CurrentAnnualXof = evaluation.Preview.Current.AnnualXof,
            TargetMonthlyXof = evaluation.Preview.Target.MonthlyXof,
            TargetAnnualXof = evaluation.Preview.Target.AnnualXof,

            RequestedBy = userId,
            RequestedByName = requester?.FullName,
            RequestedByEmail = requester?.Email,
            DirectorNote = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim()
        };

        dbContext.SubscriptionChangeRequests.Add(entity);

        // Une course sur l'index unique « une demande en attente par école » devient un 409 dans SaveChangesAsync.
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Demande de changement d'abonnement {RequestId} envoyée par l'école {SchoolId} (réduction : {IsDowngrade}).",
            entity.Id, schoolId, entity.IsDowngrade);

        return SubscriptionChangeRequestDto.From(entity);
    }
}

/// <summary>GET /tenant-subscription/change-requests — les demandes de l'école courante, la plus récente d'abord.</summary>
public record GetSubscriptionChangeRequestsQuery : IRequest<IReadOnlyList<SubscriptionChangeRequestDto>>;

public class GetSubscriptionChangeRequestsQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetSubscriptionChangeRequestsQuery, IReadOnlyList<SubscriptionChangeRequestDto>>
{
    public async Task<IReadOnlyList<SubscriptionChangeRequestDto>> Handle(
        GetSubscriptionChangeRequestsQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter et la RLS bornent la lecture à l'école du JWT.
        var requests = await dbContext.SubscriptionChangeRequests.AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);

        return requests.Select(r => SubscriptionChangeRequestDto.From(r)).ToList();
    }
}

/// <summary>POST /tenant-subscription/change-requests/{id}/cancel — le Directeur retire sa demande tant qu'elle est en attente.</summary>
public record CancelSubscriptionChangeRequestCommand(Guid Id) : IRequest<SubscriptionChangeRequestDto>, IAuditableRequest;

public class CancelSubscriptionChangeRequestCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<CancelSubscriptionChangeRequestCommand, SubscriptionChangeRequestDto>
{
    public async Task<SubscriptionChangeRequestDto> Handle(
        CancelSubscriptionChangeRequestCommand request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.SubscriptionChangeRequests.FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException("Demande de changement introuvable.");

        if (entity.Status != SubscriptionChangeStatus.Pending)
        {
            throw new BusinessRuleException("Cette demande a déjà été traitée : elle ne peut plus être retirée.", "CHANGE_REQUEST_NOT_PENDING");
        }

        entity.Status = SubscriptionChangeStatus.Cancelled;
        await dbContext.SaveChangesAsync(cancellationToken);

        return SubscriptionChangeRequestDto.From(entity);
    }
}

// ═══════════════════════════════════════════════════════════════════════ Côté Super Admin

/// <summary>GET /admin/subscription-change-requests — toutes les écoles ; filtrable par statut.</summary>
public record GetAdminSubscriptionChangeRequestsQuery(SubscriptionChangeStatus? Status)
    : IRequest<IReadOnlyList<SubscriptionChangeRequestDto>>;

public class GetAdminSubscriptionChangeRequestsQueryHandler(ISubscriptionChangeAdminStore store, IApplicationDbContext dbContext)
    : IRequestHandler<GetAdminSubscriptionChangeRequestsQuery, IReadOnlyList<SubscriptionChangeRequestDto>>
{
    public async Task<IReadOnlyList<SubscriptionChangeRequestDto>> Handle(
        GetAdminSubscriptionChangeRequestsQuery request, CancellationToken cancellationToken)
    {
        var requests = await store.ListAsync(request.Status, cancellationToken);

        // `schools` n'est pas sous RLS : le nom de l'école se lit normalement.
        var ids = requests.Select(r => r.SchoolId).Distinct().ToList();
        var names = await dbContext.Schools.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.Name, cancellationToken);

        return requests.Select(r => SubscriptionChangeRequestDto.From(r, names.GetValueOrDefault(r.SchoolId))).ToList();
    }
}

/// <summary>
/// POST /admin/subscription-change-requests/{id}/approve — le Super Admin valide la demande, une fois le règlement convenu :
/// l'abonnement et les réglages de l'école prennent l'état demandé. Écrit par une fonction SECURITY DEFINER, dans une seule
/// instruction : tout ou rien.
/// </summary>
public record ApproveSubscriptionChangeRequestCommand(Guid Id, string? Note) : IRequest<SubscriptionChangeRequestDto>, IAuditableRequest;

/// <summary>POST /admin/subscription-change-requests/{id}/reject — le Super Admin rejette la demande ; le motif est obligatoire.</summary>
public record RejectSubscriptionChangeRequestCommand(Guid Id, string Note) : IRequest<SubscriptionChangeRequestDto>, IAuditableRequest;

public class RejectSubscriptionChangeRequestCommandValidator : AbstractValidator<RejectSubscriptionChangeRequestCommand>
{
    public RejectSubscriptionChangeRequestCommandValidator()
    {
        RuleFor(c => c.Note).NotEmpty().WithMessage("Indiquez le motif du rejet : le Directeur le verra.").MaximumLength(500);
    }
}

public class ApproveSubscriptionChangeRequestCommandValidator : AbstractValidator<ApproveSubscriptionChangeRequestCommand>
{
    public ApproveSubscriptionChangeRequestCommandValidator()
    {
        RuleFor(c => c.Note).MaximumLength(500).When(c => c.Note is not null);
    }
}

public class ResolveSubscriptionChangeRequestHandlers(
    ISubscriptionChangeAdminStore store,
    IAuditLogStore auditLogStore,
    ICurrentUserService currentUser,
    IEmailSender emailSender,
    RegistrationSettings registrationSettings,
    TimeProvider timeProvider,
    ILogger<ResolveSubscriptionChangeRequestHandlers> logger)
    : IRequestHandler<ApproveSubscriptionChangeRequestCommand, SubscriptionChangeRequestDto>,
      IRequestHandler<RejectSubscriptionChangeRequestCommand, SubscriptionChangeRequestDto>
{
    public Task<SubscriptionChangeRequestDto> Handle(ApproveSubscriptionChangeRequestCommand request, CancellationToken cancellationToken) =>
        ResolveAsync(request.Id, approve: true, request.Note, cancellationToken);

    public Task<SubscriptionChangeRequestDto> Handle(RejectSubscriptionChangeRequestCommand request, CancellationToken cancellationToken) =>
        ResolveAsync(request.Id, approve: false, request.Note, cancellationToken);

    private async Task<SubscriptionChangeRequestDto> ResolveAsync(Guid id, bool approve, string? note, CancellationToken cancellationToken)
    {
        var reviewer = currentUser.UserId
            ?? throw new UnauthorizedAccessException("Aucun utilisateur authentifié.");

        var schoolId = await store.ResolveAsync(id, approve, reviewer, string.IsNullOrWhiteSpace(note) ? null : note.Trim(), cancellationToken)
            ?? throw new NotFoundException("Demande introuvable, ou déjà traitée.");

        // Attribuée à l'école concernée : le Super Admin n'a pas de SchoolId propre à qui imputer l'entrée.
        await auditLogStore.AppendAsync(
            schoolId, reviewer, "Subscriptions", approve ? "ApproveSubscriptionChange" : "RejectSubscriptionChange",
            success: true, failureReason: null, currentUser.IpAddress, timeProvider.GetUtcNow(), cancellationToken);

        logger.LogInformation(
            "Super Admin {SuperAdminId} a {Decision} la demande de changement {RequestId} de l'école {SchoolId}.",
            reviewer, approve ? "validé" : "rejeté", id, schoolId);

        var decided = await store.GetAsync(id, cancellationToken)
            ?? throw new NotFoundException("Demande introuvable.");

        var dto = SubscriptionChangeRequestDto.From(decided);

        // Après la décision, qui est déjà écrite : un e-mail parti ne se rembobine pas, et son échec (SMTP absent, adresse
        // invalide) ne doit jamais annuler une validation — il est seulement signalé au Super Admin (DirectorNotified = false).
        return dto with { DirectorNotified = await NotifyDirectorAsync(decided, dto, approve, cancellationToken) };
    }

    private async Task<bool> NotifyDirectorAsync(
        SubscriptionChangeRequest request, SubscriptionChangeRequestDto dto, bool approve, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RequestedByEmail))
        {
            logger.LogWarning(
                "Demande de changement {RequestId} décidée, mais aucune adresse de Directeur n'est consignée : aucun e-mail envoyé.",
                request.Id);
            return false;
        }

        try
        {
            var loginUrl = $"{registrationSettings.PublicBaseUrl.TrimEnd('/')}/login";
            var message = SubscriptionChangeDecisionEmail.Build(
                request.RequestedByName, request.RequestedByEmail, dto.Changes, approve, request.ReviewNote, loginUrl);

            await emailSender.SendAsync(message, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Demande de changement {RequestId} décidée, mais l'e-mail au Directeur n'a pas pu être envoyé.", request.Id);
            return false;
        }
    }
}

/// <summary>GET /admin/over-quota-schools — écoles dont l'effectif dépasse le plafond de leur tranche, pour la relance.</summary>
public record GetOverQuotaSchoolsQuery : IRequest<IReadOnlyList<OverQuotaSchoolDto>>;

public class GetOverQuotaSchoolsQueryHandler(ISubscriptionChangeAdminStore store)
    : IRequestHandler<GetOverQuotaSchoolsQuery, IReadOnlyList<OverQuotaSchoolDto>>
{
    public Task<IReadOnlyList<OverQuotaSchoolDto>> Handle(GetOverQuotaSchoolsQuery request, CancellationToken cancellationToken) =>
        store.ListOverQuotaSchoolsAsync(cancellationToken);
}
