using MediatR;

namespace SamaEcole.Application.Subjects.Commands.CreateSubject;

/// <summary>
/// POST /api/v1/subjects — openapi.yaml §SubjectCreateRequest, ticket JGK-C03.
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10) — sans quoi n'importe qui créerait une matière dans l'école d'un autre.
/// </summary>
public record CreateSubjectCommand : IRequest<SubjectResult>
{
    public required string Name { get; init; }

    /// <summary>Nom en arabe (module Coran/Franco-Arabe), saisi librement — null si non renseigné, jamais une traduction automatique.</summary>
    public string? NameAr { get; init; }

    public required string Level { get; init; }
    public decimal Coefficient { get; init; }

    /// <summary>
    /// Domaine auquel rattacher cette ligne d'évaluation (grilles APC du primaire). Null — le défaut, et
    /// le cas de toute matière du secondaire — crée une matière de premier niveau, exactement comme
    /// avant cette option. Le <see cref="Level"/> est alors celui du domaine, pas celui envoyé.
    /// </summary>
    public Guid? ParentSubjectId { get; init; }

    /// <summary>
    /// Barème propre à la ligne — la colonne « Sur » du bulletin (10, 16, 24, 40, 60…). Null suit le
    /// barème du cycle de la classe (Primaire /10, Collège &amp; Lycée /20), voir Subject.MaxScore.
    /// </summary>
    public decimal? MaxScore { get; init; }

    /// <summary>Rang d'affichage dans sa fratrie. 0 laisse le nom départager, comme avant cette option.</summary>
    public int DisplayOrder { get; init; }

    /// <summary>Entête de la 1re colonne du bulletin (« Domaines », « Activités »). Domaines parents uniquement.</summary>
    public string? Column1Header { get; init; }

    /// <summary>Entête de la 2e colonne du bulletin (« Activités », « Contrôles »). Domaines parents uniquement.</summary>
    public string? Column2Header { get; init; }
}

public record SubjectResult(
    Guid Id,
    string Name,
    string Level,
    decimal Coefficient,
    Guid? ParentSubjectId = null,
    decimal? MaxScore = null,
    int DisplayOrder = 0,
    string? NameAr = null);
