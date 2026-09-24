using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Domain.Entities;

/// <summary>
/// Suivi individuel de mémorisation coranique (module Coran/Franco-Arabe, docs/superpowers/specs/
/// 2026-09-20-franco-arabic-core-design.md §3.3). Une ligne par observation — plusieurs lignes
/// peuvent exister pour le même (StudentId, SurahNumber) au fil du temps, aucune contrainte
/// d'unicité n'est posée dans ce lot.
///
/// VERROU OPTIMISTE (AGENTS.md règle #5) : plusieurs enseignants peuvent suivre la mémorisation du
/// même élève. Le jeton est xmin, configuré dans QuranProgressConfiguration — comme Grade.
/// </summary>
public class QuranProgress : AuditableEntity, ITenantEntity
{
    public Guid SchoolId { get; set; }

    public Guid StudentId { get; set; }

    /// <summary>Numéro de Juz (1 à 30) concerné par cette observation.</summary>
    public int JuzNumber { get; set; }

    /// <summary>Numéro de Hizb (1 à 60) concerné par cette observation.</summary>
    public int HizbNumber { get; set; }

    /// <summary>Numéro de sourate (1 à 114) concernée par cette observation.</summary>
    public int SurahNumber { get; set; }

    /// <summary>Défaut <see cref="QuranMemorizationStatus.InProcess"/> : une ligne commence toujours en cours.</summary>
    public QuranMemorizationStatus Status { get; set; } = QuranMemorizationStatus.InProcess;

    /// <summary>Date de la dernière observation. Nullable : une ligne peut être créée avant toute évaluation formelle.</summary>
    public DateOnly? EvaluationDate { get; set; }

    public string? Notes { get; set; }
}
