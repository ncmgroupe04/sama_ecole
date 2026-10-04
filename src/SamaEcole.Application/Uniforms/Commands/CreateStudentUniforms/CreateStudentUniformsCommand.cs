using MediatR;

namespace SamaEcole.Application.Uniforms.Commands.CreateStudentUniforms;

/// <summary>
/// Enregistre les besoins en tenues d'une inscription — tout ou rien. L'élève et l'année viennent de l'inscription,
/// jamais du client. Aucune écriture dans Finance : la facturation attendue se LIT dans les lignes de frais.
/// </summary>
public record CreateStudentUniformsCommand : IRequest<IReadOnlyList<StudentUniformDto>>
{
    public Guid EnrollmentId { get; init; }

    public required IReadOnlyList<StudentUniformRequestLine> Lines { get; init; }
}
