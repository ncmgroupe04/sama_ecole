using MediatR;

namespace SamaEcole.Application.Schools.Commands.UpdateCurrentSchool;

/// <summary>
/// PUT /schools/current — le Directeur corrige l'identité de son établissement. Aucun <c>schoolId</c>
/// dans la charge utile : l'établissement visé est TOUJOURS celui du JWT (AGENTS.md règle #10).
/// </summary>
public record UpdateCurrentSchoolCommand(
    string Name,
    string? Address,
    string? Phone,
    string? LogoUrl) : IRequest<SchoolProfileDto>;
