using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Teachers.Queries.GetTeacherImportTemplate;

/// <summary>
/// GET /api/v1/teachers/import/template — bouton « Télécharger le modèle Excel d'exemple » de l'écran
/// d'import de masse des enseignants. Les matières listées dans la feuille de référence sont celles de
/// l'école COURANTE : deux écoles ne téléchargent jamais le même fichier.
/// </summary>
public record GetTeacherImportTemplateQuery : IRequest<TeacherImportTemplateResult>;

public record TeacherImportTemplateResult(byte[] Content, string FileName);

public class GetTeacherImportTemplateQueryHandler(
    IApplicationDbContext dbContext, ITeacherImportTemplateGenerator generator)
    : IRequestHandler<GetTeacherImportTemplateQuery, TeacherImportTemplateResult>
{
    public async Task<TeacherImportTemplateResult> Handle(
        GetTeacherImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var subjects = await dbContext.Subjects.AsNoTracking()
            .OrderBy(s => s.Level).ThenBy(s => s.Name)
            .Select(s => new { s.Name, s.Level })
            .ToListAsync(cancellationToken);

        var content = generator.Generate(subjects.Select(s => (s.Name, s.Level)).ToList());

        return new TeacherImportTemplateResult(content, "Modele-Import-Enseignants.xlsx");
    }
}
