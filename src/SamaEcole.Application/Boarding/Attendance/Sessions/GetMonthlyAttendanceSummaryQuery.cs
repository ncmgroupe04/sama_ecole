using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Application.Internat;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Application.Boarding.Attendance.Sessions;

/// <summary>
/// Une ligne du récapitulatif : les jours pointés du mois, répartis. <paramref name="Present"/> + <paramref name="Absent"/> +
/// <paramref name="OnLeave"/> + <paramref name="NotRecorded"/> = nombre de jours pris en compte. Une absence notée « En permission »
/// compte en <paramref name="OnLeave"/>, pas en absence : la pension ne facture pas un repas qu'un permissionnaire n'a pas pris.
/// </summary>
public record MonthlySummaryRowDto(
    Guid StudentId, string StudentName, string? StudentNameAr, string Matricule, string? Detail,
    int Present, int Absent, int OnLeave, int NotRecorded);

/// <summary>
/// Récapitulatif mensuel d'une session pour un groupe. <paramref name="Days"/> : les jours où la session a été pointée pour le groupe
/// — un jour sans aucun pointage n'est pas un jour d'appel, il n'entre dans aucun compte.
/// </summary>
public record MonthlySummaryDto(
    DateOnly MonthStart,
    Guid SessionId,
    string SessionName,
    AttendanceAudience Audience,
    Guid GroupId,
    string GroupName,
    IReadOnlyList<DateOnly> Days,
    IReadOnlyList<MonthlySummaryRowDto> Rows);

/// <summary>
/// GET /api/v1/boarding/attendance/monthly-summary?month=YYYY-MM&amp;sessionId=&amp;groupId= — par élève, présents / absents / en permission /
/// non pointés. <paramref name="MonthStart"/> est le premier jour du mois. Le groupe est un pavillon (internes), une classe ou un
/// Oustaz selon la session ; un Oustaz ne lit que sa Halqa.
/// </summary>
public record GetMonthlyAttendanceSummaryQuery(DateOnly MonthStart, Guid SessionId, Guid? GroupId) : IRequest<MonthlySummaryDto>;

public class GetMonthlyAttendanceSummaryQueryValidator : AbstractValidator<GetMonthlyAttendanceSummaryQuery>
{
    public GetMonthlyAttendanceSummaryQueryValidator()
    {
        RuleFor(x => x.SessionId).NotEmpty();
        RuleFor(x => x.MonthStart).Must(d => d.Day == 1).WithMessage("Indiquez un mois (AAAA-MM).");
    }
}

public class GetMonthlyAttendanceSummaryQueryHandler(IApplicationDbContext dbContext, HalqaScopeAuthorizer scope)
    : IRequestHandler<GetMonthlyAttendanceSummaryQuery, MonthlySummaryDto>
{
    public async Task<MonthlySummaryDto> Handle(GetMonthlyAttendanceSummaryQuery request, CancellationToken cancellationToken)
    {
        var session = await SessionRoster.GetAnySessionAsync(dbContext, request.SessionId, cancellationToken);
        var groupId = await SessionRoster.ResolveGroupAsync(scope, session, request.GroupId, cancellationToken);
        var roster = await SessionRoster.LoadAsync(dbContext, session, groupId, cancellationToken);

        var from = request.MonthStart;
        var to = from.AddMonths(1).AddDays(-1);
        var studentIds = roster.Members.Select(m => m.StudentId).ToList();

        var rows = await dbContext.BoardingAttendances.AsNoTracking()
            .Where(a => a.SessionId == session.Id && a.Date >= from && a.Date <= to && studentIds.Contains(a.StudentId))
            .Select(a => new { a.StudentId, a.Date, a.IsPresent, a.Note })
            .ToListAsync(cancellationToken);

        var days = rows.Select(r => r.Date).Distinct().Order().ToList();
        var byStudent = rows.ToLookup(r => r.StudentId);

        var lines = roster.Members.Select(m =>
        {
            var own = byStudent[m.StudentId].ToList();
            var present = own.Count(r => r.IsPresent);
            var onLeave = own.Count(r => !r.IsPresent && IsOnLeaveNote(r.Note));
            var absent = own.Count(r => !r.IsPresent) - onLeave;
            return new MonthlySummaryRowDto(
                m.StudentId, m.FullName, m.FullNameAr, m.Matricule, m.Detail,
                present, absent, onLeave, days.Count - own.Count);
        }).ToList();

        return new MonthlySummaryDto(from, session.Id, session.Name, session.Audience, roster.GroupId, roster.GroupName, days, lines);
    }

    /// <summary>La note « En permission » (posée par le serveur, ou saisie : « En permission — famille ») marque une absence justifiée par une sortie.</summary>
    private static bool IsOnLeaveNote(string? note) =>
        note is not null && note.Contains(AttendanceRules.OnLeaveNote, StringComparison.OrdinalIgnoreCase);
}
