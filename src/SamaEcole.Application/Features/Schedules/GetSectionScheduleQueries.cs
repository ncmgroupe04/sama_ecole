using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.ReportCards;

namespace SamaEcole.Application.Features.Schedules;

/// <summary>Une demi-journée et les heures de chaque section qu'elle porte.</summary>
public record HalfDayDto(DayOfWeek DayOfWeek, string DayLabel, DayHalf Half, decimal FrenchHours, decimal ArabicHours, bool IsMixed);

/// <summary>
/// Répartition français / arabe d'un emploi du temps (voir <see cref="SectionScheduleAnalyzer"/>). La section d'un
/// créneau est celle du POLE de sa matière (Subject.SectionType) : aucune donnée nouvelle à saisir, une matière
/// jamais classée compte comme française.
/// </summary>
public record SectionScheduleDto(
    decimal FrenchHours,
    decimal ArabicHours,
    SectionPattern Pattern,
    bool CoversBothSections,
    IReadOnlyList<HalfDayDto> HalfDays,
    IReadOnlyList<string> Notices)
{
    public static SectionScheduleDto From(SectionScheduleAnalysis analysis) => new(
        analysis.FrenchHours,
        analysis.ArabicHours,
        analysis.Pattern,
        analysis.CoversBothSections,
        analysis.HalfDays
            .Select(h => new HalfDayDto(h.Day, SectionScheduleAnalyzer.DayName(h.Day), h.Half, h.FrenchHours, h.ArabicHours, h.IsMixed))
            .ToList(),
        analysis.Notices);
}

/// <summary>GET /api/v1/schedules/classroom/{id}/sections — alternance français / arabe d'une classe. Lecture seule.</summary>
public record GetClassroomSectionScheduleQuery(Guid ClassroomId) : IRequest<SectionScheduleDto>;

/// <summary>
/// GET /api/v1/schedules/teacher/{id}/sections — alternance d'un enseignant ; <c>CoversBothSections</c> repère
/// l'enseignant « bilingue », qui enseigne dans les deux sections.
/// </summary>
public record GetTeacherSectionScheduleQuery(Guid TeacherId) : IRequest<SectionScheduleDto>;

public class GetSectionScheduleQueryHandlers(IApplicationDbContext context)
    : IRequestHandler<GetClassroomSectionScheduleQuery, SectionScheduleDto>,
      IRequestHandler<GetTeacherSectionScheduleQuery, SectionScheduleDto>
{
    public Task<SectionScheduleDto> Handle(GetClassroomSectionScheduleQuery request, CancellationToken cancellationToken) =>
        AnalyzeAsync(context.ScheduleSlots.AsNoTracking().Where(s => s.ClassroomId == request.ClassroomId), cancellationToken);

    public Task<SectionScheduleDto> Handle(GetTeacherSectionScheduleQuery request, CancellationToken cancellationToken) =>
        AnalyzeAsync(context.ScheduleSlots.AsNoTracking().Where(s => s.TeacherId == request.TeacherId), cancellationToken);

    private static async Task<SectionScheduleDto> AnalyzeAsync(
        IQueryable<Domain.Entities.ScheduleSlot> slots, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS bornent déjà à l'école courante.
        var rows = await slots
            .Select(s => new { s.DayOfWeek, s.StartTime, s.EndTime, s.Subject.SectionType })
            .ToListAsync(cancellationToken);

        var analysis = SectionScheduleAnalyzer.Analyze(
            rows.Select(r => new SectionSlot(r.DayOfWeek, r.StartTime, r.EndTime, ReportSections.Of(r.SectionType))));

        return SectionScheduleDto.From(analysis);
    }
}
