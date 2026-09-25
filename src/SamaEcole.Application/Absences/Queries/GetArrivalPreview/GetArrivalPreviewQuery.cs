using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SamaEcole.Application.Attendance.EntryTickets;
using SamaEcole.Application.Common.Exceptions;
using SamaEcole.Application.Common.Interfaces;

namespace SamaEcole.Application.Absences.Queries.GetArrivalPreview;

/// <summary>
/// GET /api/v1/absences/arrival-preview?studentId=&amp;date=&amp;arrivalTime= — ce que le billet d'entrée va
/// régulariser pour cette heure d'arrivée (Complément N°5 bis) : cours manqués, retard sur le cours en cours, cours
/// visé, durée totale. Le calcul est celui de l'ÉMISSION (<see cref="ArrivalPlanner"/>) : l'écran affiche ce que
/// le serveur calcule et n'a aucune règle à recopier. Lecture seule (règle #7) ; l'école vient du JWT.
/// </summary>
public record GetArrivalPreviewQuery(Guid StudentId, DateOnly? Date, TimeOnly ArrivalTime)
    : IRequest<ArrivalPreviewDto>;

/// <param name="Minutes">Durée du cours manqué (minutes).</param>
public record ArrivalPreviewSlotDto(Guid SlotId, string Label, string SubjectName, int Minutes);

/// <param name="LateMinutes">Minutes de retard sur ce cours (arrivée − début).</param>
public record ArrivalPreviewLateDto(Guid SlotId, string Label, string SubjectName, int LateMinutes);

public record ArrivalPreviewDto(
    IReadOnlyList<ArrivalPreviewSlotDto> MissedSlots,
    ArrivalPreviewLateDto? InProgress,
    Guid? TargetSlotId,
    int MissedMinutes,
    int LateMinutes,
    int TotalMinutes);

public class GetArrivalPreviewQueryValidator : AbstractValidator<GetArrivalPreviewQuery>
{
    public GetArrivalPreviewQueryValidator() => RuleFor(x => x.StudentId).NotEmpty();
}

public class GetArrivalPreviewQueryHandler(
    IApplicationDbContext dbContext,
    ArrivalPlanner planner,
    TimeProvider timeProvider)
    : IRequestHandler<GetArrivalPreviewQuery, ArrivalPreviewDto>
{
    public async Task<ArrivalPreviewDto> Handle(GetArrivalPreviewQuery request, CancellationToken cancellationToken)
    {
        var classroomId = await dbContext.Students.AsNoTracking()
            .Where(s => s.Id == request.StudentId)
            .Select(s => (Guid?)s.ClassroomId)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException("Student", request.StudentId.ToString());

        var date = request.Date ?? DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var plan = await planner.PlanAsync(classroomId, request.StudentId, date, request.ArrivalTime, cancellationToken);
        var coverage = plan.Coverage;

        var missed = coverage.MissedSlotIds
            .Select(id => plan.Slots[id])
            .Select(s => new ArrivalPreviewSlotDto(s.SlotId, s.Label, s.SubjectName, s.Minutes))
            .ToList();

        var inProgress = coverage.InProgressSlotId is { } inProgressId
            ? plan.Slots[inProgressId] : null;

        return new ArrivalPreviewDto(
            missed,
            inProgress is null
                ? null
                : new ArrivalPreviewLateDto(inProgress.SlotId, inProgress.Label, inProgress.SubjectName, coverage.LateMinutes),
            coverage.TargetSlotId,
            coverage.MissedMinutes,
            coverage.LateMinutes,
            coverage.TotalMinutes);
    }
}
