using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.StateIntegration.Queries.GetMutationCertificates;

/// <summary>
/// GET /api/v1/state-integration/certificates — les certificats de mutation délivrés par
/// l'établissement, du plus récent au plus ancien (Volume 1 §23.5). Sert l'écran
/// <c>/integration-etatique</c> : réimpression et révocation.
///
/// Les certificats RÉVOQUÉS restent dans la liste (marqués), jamais masqués : une pièce révoquée doit
/// rester traçable — c'est même la raison d'être de la révocation plutôt que de la suppression.
/// Pagination selon la convention transverse (page/pageSize, 20 par défaut, 100 max).
/// </summary>
public record GetMutationCertificatesQuery : IRequest<PaginatedMutationCertificates>
{
    /// <summary>Restreint à un élève (fiche élève). Null = tout l'établissement.</summary>
    public Guid? StudentId { get; init; }

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;
}

public record PaginatedMutationCertificates(
    IReadOnlyList<MutationCertificateListItem> Items,
    int TotalCount,
    int Page,
    int PageSize);

public record MutationCertificateListItem(
    Guid Id,
    string CertificateNumber,
    Guid StudentId,
    string StudentFullName,
    string StudentMatricule,
    string SchoolYearLabel,
    string ClassroomNameSnapshot,
    string Reason,
    string? DestinationSchoolName,
    string? DestinationCity,
    bool WasFinanciallyClear,
    DateOnly IssuedOn,
    DateTimeOffset? RevokedAt,
    string? RevocationReason)
{
    public bool IsRevoked => RevokedAt is not null;
}

public class GetMutationCertificatesQueryHandler(IApplicationDbContext dbContext)
    : IRequestHandler<GetMutationCertificatesQuery, PaginatedMutationCertificates>
{
    public async Task<PaginatedMutationCertificates> Handle(
        GetMutationCertificatesQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);

        // Aucun filtre SchoolId : Global Query Filter + RLS bornent déjà à l'école courante (règle #2).
        var query = dbContext.StudentMutationCertificates.AsNoTracking();

        if (request.StudentId is { } studentId)
        {
            query = query.Where(c => c.StudentId == studentId);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        // Projection SQL vers un type intermédiaire (l'enum reste l'enum), puis mise en forme EN
        // MÉMOIRE : `Reason.ToString()` n'a pas à être traduit par le fournisseur, et le reste du
        // codebase suit cette même prudence pour les enums convertis en texte.
        var rows = await query
            .OrderByDescending(c => c.IssuedOn).ThenByDescending(c => c.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Join(dbContext.Students.AsNoTracking(),
                c => c.StudentId, s => s.Id, (c, s) => new { c, s })
            .Join(dbContext.SchoolYears.AsNoTracking(),
                pair => pair.c.SchoolYearId, y => y.Id, (pair, y) => new
                {
                    pair.c.Id,
                    pair.c.CertificateNumber,
                    StudentId = pair.s.Id,
                    StudentFullName = pair.s.FullName,
                    StudentMatricule = pair.s.Matricule,
                    SchoolYearLabel = y.Label,
                    pair.c.ClassroomNameSnapshot,
                    pair.c.Reason,
                    pair.c.DestinationSchoolName,
                    pair.c.DestinationCity,
                    pair.c.WasFinanciallyClear,
                    pair.c.IssuedOn,
                    pair.c.RevokedAt,
                    pair.c.RevocationReason
                })
            .ToListAsync(cancellationToken);

        var items = rows.Select(r => new MutationCertificateListItem(
            r.Id, r.CertificateNumber, r.StudentId, r.StudentFullName, r.StudentMatricule,
            r.SchoolYearLabel, r.ClassroomNameSnapshot, r.Reason.ToString(),
            r.DestinationSchoolName, r.DestinationCity, r.WasFinanciallyClear,
            r.IssuedOn, r.RevokedAt, r.RevocationReason)).ToList();

        return new PaginatedMutationCertificates(items, totalCount, page, pageSize);
    }
}
