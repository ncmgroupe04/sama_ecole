using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Entities;

namespace SamaEcole.Application.HourVolumes;

/// <summary>D'où vient le volume de référence d'une matière.</summary>
public enum HourNormSource
{
    None,
    Template,

    /// <summary>Réglage de l'école pour le niveau, toutes séries confondues.</summary>
    SchoolGrade,

    /// <summary>Réglage de l'école pour le niveau ET la série.</summary>
    SchoolSeries
}

public sealed record ResolvedHourNorm(decimal? Hours, HourNormSource Source, string? OptionGroup);

/// <summary>
/// Volume de référence effectif d'une matière pour un niveau et une série : le réglage de l'école pour la série, sinon
/// pour le niveau, sinon la grille codée (<see cref="WeeklyHourTemplates"/>). Les réglages sont chargés une fois.
/// </summary>
public sealed class WeeklyHourNormResolver
{
    private readonly Dictionary<(string Grade, string? Series, Guid SubjectId), WeeklyHourNorm> _overrides;
    private readonly Dictionary<Guid, uint> _rowVersions;

    private WeeklyHourNormResolver(IReadOnlyCollection<(WeeklyHourNorm Norm, uint RowVersion)> overrides)
    {
        _overrides = overrides.ToDictionary(o => (o.Norm.GradeLevel, o.Norm.Series, o.Norm.SubjectId), o => o.Norm);
        _rowVersions = overrides.ToDictionary(o => o.Norm.Id, o => o.RowVersion);
    }

    public static async Task<WeeklyHourNormResolver> LoadAsync(IApplicationDbContext dbContext, CancellationToken cancellationToken)
    {
        var rows = await dbContext.WeeklyHourNorms.AsNoTracking()
            .Select(n => new { Norm = n, RowVersion = EF.Property<uint>(n, "xmin") })
            .ToListAsync(cancellationToken);
        return new(rows.Select(r => (r.Norm, r.RowVersion)).ToList());
    }

    public uint RowVersionOf(Guid overrideId) => _rowVersions[overrideId];

    public WeeklyHourNorm? Override(string grade, string? series, Guid subjectId)
        => _overrides.GetValueOrDefault((grade, series, subjectId));

    public ResolvedHourNorm Resolve(string? grade, string? series, Guid subjectId, string subjectName)
    {
        if (grade is null) return new ResolvedHourNorm(null, HourNormSource.None, null);

        var line = WeeklyHourTemplates.LineFor(grade, series, subjectName);
        var group = line?.OptionGroup;

        if (series is not null && Override(grade, series, subjectId) is { } bySeries)
            return new ResolvedHourNorm(bySeries.WeeklyHours, HourNormSource.SchoolSeries, group);

        if (Override(grade, null, subjectId) is { } byGrade)
            return new ResolvedHourNorm(byGrade.WeeklyHours, HourNormSource.SchoolGrade, group);

        return line is null
            ? new ResolvedHourNorm(null, HourNormSource.None, null)
            : new ResolvedHourNorm(line.Hours, HourNormSource.Template, group);
    }
}
