using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.ReportCards.Commands.ApplyCouncilDecisionProposals;

/// <summary>
/// POST /api/v1/report-cards/council-decisions/apply-proposals — « Appliquer les propositions » (Évolution N°7) :
/// pour chaque élève de la classe SANS décision sur la période <see cref="TermId"/>, enregistre la décision de fin
/// d'année proposée d'après sa moyenne annuelle et les seuils de l'école. Ne remplace JAMAIS une décision déjà
/// prise par le conseil ; un élève sans moyenne annuelle reste sans décision. Idempotente.
/// </summary>
public record ApplyCouncilDecisionProposalsCommand(Guid ClassroomId, Guid TermId)
    : IRequest<ApplyCouncilDecisionProposalsResult>, IAuditableRequest;

/// <param name="Applied">Décisions enregistrées.</param>
/// <param name="AlreadyDecided">Élèves dont la décision du conseil était déjà saisie (conservée).</param>
/// <param name="WithoutAverage">Élèves sans moyenne annuelle : aucune proposition possible.</param>
public record ApplyCouncilDecisionProposalsResult(int Applied, int AlreadyDecided, int WithoutAverage);

public class ApplyCouncilDecisionProposalsCommandValidator : AbstractValidator<ApplyCouncilDecisionProposalsCommand>
{
    public ApplyCouncilDecisionProposalsCommandValidator()
    {
        RuleFor(x => x.ClassroomId).NotEmpty();
        RuleFor(x => x.TermId).NotEmpty();
    }
}

public class ApplyCouncilDecisionProposalsCommandHandler(
    IApplicationDbContext dbContext, ITenantProvider tenantProvider, ReportCardDataService dataService)
    : IRequestHandler<ApplyCouncilDecisionProposalsCommand, ApplyCouncilDecisionProposalsResult>
{
    public async Task<ApplyCouncilDecisionProposalsResult> Handle(
        ApplyCouncilDecisionProposalsCommand request, CancellationToken cancellationToken)
    {
        var schoolId = tenantProvider.CurrentSchoolId
            ?? throw new UnauthorizedAccessException("Aucun établissement associé à l'utilisateur courant.");

        if (!await dbContext.Classrooms.AnyAsync(c => c.Id == request.ClassroomId, cancellationToken))
        {
            throw new KeyNotFoundException($"Classe {request.ClassroomId} introuvable dans votre établissement.");
        }

        if (!await dbContext.Terms.AnyAsync(t => t.Id == request.TermId, cancellationToken))
        {
            throw new KeyNotFoundException($"Période {request.TermId} introuvable dans votre établissement.");
        }

        var studentIds = await dbContext.Students.AsNoTracking()
            .Where(s => s.ClassroomId == request.ClassroomId)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        // Suivies : une décision posée ici passe par le verrou xmin normal d'EF (409 si le conseil saisit en même temps).
        var remarks = (await dbContext.ReportCardRemarks
                .Where(r => r.TermId == request.TermId && studentIds.Contains(r.StudentId))
                .ToListAsync(cancellationToken))
            .ToDictionary(r => r.StudentId);

        int applied = 0, alreadyDecided = 0, withoutAverage = 0;
        foreach (var studentId in studentIds)
        {
            if (remarks.TryGetValue(studentId, out var existing) && existing.CouncilDecision is not null)
            {
                alreadyDecided++;
                continue;
            }

            // Même calcul que le bulletin et le PV annuel : moyenne annuelle et proposition du service de bulletin.
            var card = await dataService.BuildAsync(studentId, request.TermId, cancellationToken);
            if (card.ProposedCouncilDecision is not { } proposal)
            {
                withoutAverage++;
                continue;
            }

            if (existing is null)
            {
                existing = new ReportCardRemark { SchoolId = schoolId, StudentId = studentId, TermId = request.TermId };
                dbContext.ReportCardRemarks.Add(existing);
            }

            existing.CouncilDecision = proposal;
            applied++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ApplyCouncilDecisionProposalsResult(applied, alreadyDecided, withoutAverage);
    }
}
