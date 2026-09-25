using SamaEcole.Application.ClassSubjects;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Classrooms.Commands.CreateClassroom;

/// <summary>
/// POST /api/v1/classrooms — openapi.yaml, ticket JGK-C02.
/// Le SchoolId n'est PAS ici : il est lu dans le JWT via ITenantProvider, jamais accepté du client
/// (AGENTS.md règle #10) — sans quoi n'importe qui créerait une classe dans l'école d'un autre.
/// </summary>
public record CreateClassroomCommand : IRequest<CreateClassroomResult>
{
    public required string Name { get; init; }
    public required string Level { get; init; }
    public int Capacity { get; init; }

    /// <summary>
    /// Classe passerelle / accélérée (option, faux par défaut) : l'année valide DEUX niveaux. Absent du
    /// corps de requête d'un client existant → false, exactement le comportement d'avant l'option.
    /// </summary>
    public bool IsAccelerated { get; init; }

    /// <summary>Second niveau validé, obligatoire quand — et seulement quand — <see cref="IsAccelerated"/>.</summary>
    public string? TargetLevel { get; init; }

    /// <summary>
    /// Série du lycée (S1, S2, L1a, L2, STEG, LA… — voir LyceeSeries) — Évolution N°4. Optionnelle ; refusée hors lycée. Absente du
    /// corps d'un client existant → aucune série, exactement le comportement d'avant.
    /// </summary>
    public string? Series { get; init; }
}

/// <summary>
/// <see cref="Cycle"/> est DÉRIVÉ du niveau par le handler (ClassroomCycle), jamais envoyé par le
/// client : il est renvoyé ici pour que l'appelant sache immédiatement quel barème et quel en-tête de
/// bulletin sa classe vient de recevoir — il n'a aucun moyen de le déduire lui-même.
/// <see cref="Template"/> dit ce que l'injection du modèle de la série a posé (Évolution N°6).
/// </summary>
public record CreateClassroomResult(
    Guid Id, string Name, string Level, int Capacity, CycleType Cycle,
    bool IsAccelerated = false, string? TargetLevel = null, string? Series = null,
    ClassTemplateReport? Template = null);
