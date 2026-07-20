using SamaEcole.Application.Common.Interfaces;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.Students.Queries.GetStudentImportTemplate;

/// <summary>
/// GET /api/v1/students/import/template — bouton « Télécharger le modèle Excel d'exemple » de l'écran
/// d'import de masse. Les classes listées dans la feuille de référence (et la liste déroulante Classe)
/// sont celles de l'école COURANTE : deux écoles ne téléchargent jamais le même fichier.
/// </summary>
public record GetStudentImportTemplateQuery : IRequest<StudentImportTemplateResult>;

public record StudentImportTemplateResult(byte[] Content, string FileName);

public class GetStudentImportTemplateQueryHandler(
    IApplicationDbContext dbContext, IStudentImportTemplateGenerator generator)
    : IRequestHandler<GetStudentImportTemplateQuery, StudentImportTemplateResult>
{
    public async Task<StudentImportTemplateResult> Handle(
        GetStudentImportTemplateQuery request, CancellationToken cancellationToken)
    {
        var classroomNames = await dbContext.Classrooms.AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => c.Name)
            .ToListAsync(cancellationToken);

        var content = generator.Generate(classroomNames);

        return new StudentImportTemplateResult(content, "Modele-Import-Eleves.xlsx");
    }
}
