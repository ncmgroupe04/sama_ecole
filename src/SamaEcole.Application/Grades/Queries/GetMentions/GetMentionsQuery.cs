using MediatR;

namespace SamaEcole.Application.Grades.Queries.GetMentions;

/// <summary>
/// GET /api/v1/grades/mentions — ticket JGK-G02. Les mentions personnalisées par l'établissement, ou
/// les valeurs par défaut (barème/fractions) tant qu'aucune n'a été créée — une école a TOUJOURS des
/// mentions, même si personne n'a encore ouvert cet écran (même principe que SchoolSettings).
/// </summary>
public record GetMentionsQuery : IRequest<IReadOnlyList<MentionDto>>;

/// <summary><see cref="Id"/> est null pour une mention par défaut, non encore stockée.</summary>
public record MentionDto(Guid? Id, string Label, decimal MinAverage);
