using SamaEcole.Application.Grades.Queries.GetMentions;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.CreateMention;

/// <summary>
/// POST /grades/mentions — ticket JGK-G02. Réservé au Directeur (docs/Volume_7_Security.md
/// « Paramètres de l'école » : notation, mentions). Dès la première mention créée, l'école cesse de
/// recevoir les valeurs par défaut (GetMentionsQueryHandler) : elle possède désormais SA liste.
/// </summary>
public record CreateMentionCommand(string Label, decimal MinAverage) : IRequest<MentionDto>;
