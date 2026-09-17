using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Subjects.Commands.UpdateSubject;

public class UpdateSubjectCommandHandler(IApplicationDbContext dbContext)
    : IRequestHandler<UpdateSubjectCommand, UpdateSubjectResult>
{
    public async Task<UpdateSubjectResult> Handle(UpdateSubjectCommand request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà la recherche à l'école courante : viser
        // une matière d'une autre école renvoie 404, jamais une modification silencieuse.
        var subject = await dbContext.Subjects
            .FirstOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Matière {request.Id} introuvable.");

        // Rattachement à un domaine : mêmes invariants qu'à la création, plus celui que la création ne
        // peut pas rencontrer — une matière qui porte déjà des activités ne peut pas en devenir une.
        if (request.ParentSubjectId is not null && subject.ParentSubjectId != request.ParentSubjectId)
        {
            await SubjectHierarchyGuard.EnsureCanBecomeChildAsync(dbContext, subject.Id, cancellationToken);
        }

        var level = await SubjectHierarchyGuard.ResolveLevelForParentAsync(
            dbContext, request.ParentSubjectId, subject.Id, request.Level.Trim(), cancellationToken);

        // Cœur du verrou optimiste (AGENTS.md règle #5). Une violation de l'index unique
        // (SchoolId, Level, ParentSubjectId, Name, IsDeleted) — un même (niveau, domaine, nom) déjà
        // pris — est traduite au même endroit par SaveChangesAsync.
        dbContext.SetOriginalConcurrencyToken(subject, request.RowVersion);

        var previousLevel = subject.Level;

        subject.Name = request.Name.Trim();
        subject.NameAr = Trimmed(request.NameAr);
        subject.Level = level;
        subject.Coefficient = request.Coefficient;
        subject.ParentSubjectId = request.ParentSubjectId;
        subject.MaxScore = request.MaxScore;
        subject.DisplayOrder = request.DisplayOrder;

        // Les entêtes de colonnes qualifient la GRILLE, pas une ligne : une activité n'en porte aucun
        // (même règle qu'à la création).
        subject.Column1Header = request.ParentSubjectId is null ? Trimmed(request.Column1Header) : null;
        subject.Column2Header = request.ParentSubjectId is null ? Trimmed(request.Column2Header) : null;

        // Déplacer un DOMAINE d'un niveau à l'autre emmène ses activités avec lui. Les laisser derrière
        // les rendrait invisibles sur les deux bulletins à la fois : celui de l'ancien niveau n'a plus
        // leur domaine pour les regrouper, celui du nouveau ne les voit pas. L'écran présente d'ailleurs
        // le domaine comme propriétaire de ses lignes — c'est le comportement qu'il annonce.
        if (subject.ParentSubjectId is null && !string.Equals(previousLevel, subject.Level, StringComparison.Ordinal))
        {
            var children = await dbContext.Subjects
                .Where(s => s.ParentSubjectId == subject.Id)
                .ToListAsync(cancellationToken);

            foreach (var child in children)
            {
                child.Level = subject.Level;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        var newRowVersion = await dbContext.Subjects.AsNoTracking()
            .Where(s => s.Id == subject.Id)
            .Select(s => EF.Property<uint>(s, "xmin"))
            .FirstAsync(cancellationToken);

        return new UpdateSubjectResult(
            subject.Id, subject.Name, subject.Level, subject.Coefficient, newRowVersion,
            subject.ParentSubjectId, subject.MaxScore, subject.DisplayOrder, subject.NameAr);
    }

    /// <summary>Entête vide ou blanche = « pas d'entête personnalisé » (null), jamais une chaîne vide stockée.</summary>
    private static string? Trimmed(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
