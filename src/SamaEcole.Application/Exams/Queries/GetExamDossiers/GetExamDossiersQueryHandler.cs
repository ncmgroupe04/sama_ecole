using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Exams.Queries.GetExamDossiers;

public class GetExamDossiersQueryHandler(IApplicationDbContext dbContext, ExamDossierScopeAuthorizer scopeAuthorizer)
    : IRequestHandler<GetExamDossiersQuery, PaginatedExamDossiers>
{
    private const int MaxPageSize = 100;

    public async Task<PaginatedExamDossiers> Handle(GetExamDossiersQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        // Portée JGK-J08 : null pour Directeur/Secrétariat (non bornés), liste des classes assignées
        // pour un Enseignant — jamais appliquée en filtrant après coup, toujours avant la pagination.
        var readableClassroomIds = await scopeAuthorizer.GetReadableClassroomIdsAsync(cancellationToken);

        // Aucun filtre SchoolId ici : le Global Query Filter l'applique déjà (AGENTS.md règle #2).
        var query = dbContext.ExamDossiers.AsNoTracking();

        if (readableClassroomIds is not null)
        {
            query = query.Where(d => readableClassroomIds.Contains(d.ClassroomId));
        }

        if (request.ExamSessionId is { } examSessionId)
        {
            query = query.Where(d => d.ExamSessionId == examSessionId);
        }

        if (request.ClassroomId is { } classroomId)
        {
            // Une classe demandée hors de la portée de l'Enseignant est un refus explicite (403), pas
            // une simple absence de résultat — même règle que GetExamDossierDetailQueryHandler.
            if (readableClassroomIds is not null && !readableClassroomIds.Contains(classroomId))
            {
                throw new ForbiddenException(
                    "Vous n'êtes pas assigné à cette classe : vous ne pouvez pas consulter ses dossiers.");
            }

            query = query.Where(d => d.ClassroomId == classroomId);
        }

        if (request.Status is { } status)
        {
            query = query.Where(d => d.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            // ToLower() plutôt qu'EF.Functions.ILike (extension Npgsql) : SamaEcole.Application ne
            // doit connaître aucun provider (AGENTS.md règle #1), même arbitrage que GetInventoryItems.
            var search = request.Search.Trim().ToLower();

            query = query.Where(d =>
                dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault()!.ToLower().Contains(search)
                || (d.CandidateNumber != null && d.CandidateNumber.ToLower().Contains(search)));
        }

        var totalCount = await CountOrZeroAsync(query, cancellationToken);

        var items = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query
                .OrderBy(d => d.CandidateNumber == null)
                .ThenBy(d => d.CandidateNumber)
                .ThenBy(d => d.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(d => new ExamDossierListItem(
                    d.Id,
                    d.ExamSessionId,
                    d.StudentId,
                    dbContext.Students.Where(s => s.Id == d.StudentId).Select(s => s.FullName).FirstOrDefault() ?? "Élève supprimé",
                    d.ClassroomId,
                    dbContext.Classrooms.Where(c => c.Id == d.ClassroomId).Select(c => c.Name).FirstOrDefault() ?? "Classe supprimée",
                    d.CandidateNumber,
                    d.ExamCenterName,
                    d.BirthCertificatePresent,
                    d.CivilStatusConforming,
                    d.Status.ToString(),
                    EF.Property<uint>(d, "xmin"),
                    d.ExamCenterCode,
                    d.TableNumber,
                    d.CivilRegistryDocumentStatus.ToString())),
            cancellationToken);

        return new PaginatedExamDossiers(items, totalCount, page, pageSize);
    }

    private async Task<int> CountOrZeroAsync(IQueryable<Domain.Entities.ExamDossier> query, CancellationToken cancellationToken)
    {
        var counted = await dbContext.ToListOrEmptyOnMissingTableAsync(
            query.GroupBy(_ => 1).Select(group => group.Count()), cancellationToken);

        return counted.FirstOrDefault();
    }
}
