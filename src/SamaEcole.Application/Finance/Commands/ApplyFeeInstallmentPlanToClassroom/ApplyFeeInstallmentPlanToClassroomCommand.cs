using MediatR;

namespace SamaEcole.Application.Finance.Commands.ApplyFeeInstallmentPlanToClassroom;

/// <summary>
/// POST /finance/classrooms/{classroomId}/installment-plan — applique le MÊME modèle d'échéancier
/// (répartition en pourcentages + décalages de jours depuis aujourd'hui) à chaque inscription active
/// de la classe (Étape 5, "échéancier par classe"). Chaque inscription reçoit son PROPRE
/// FeeInstallmentPlan calculé sur son propre TotalDue — pas un plan partagé — même matérialisation
/// ligne par ligne que ApplyStandardFeeCommand pour le barème.
/// </summary>
public record ApplyFeeInstallmentPlanToClassroomCommand(
    Guid ClassroomId,
    string? Reason,
    IReadOnlyList<InstallmentTemplateLine> Template) : IRequest<ApplyFeeInstallmentPlanToClassroomResult>;

/// <summary>
/// <paramref name="Percentage"/> est une fraction de <c>Enrollment.TotalDue</c> (ex. 0.4 pour 40 %) ;
/// <paramref name="OffsetDays"/> est le nombre de jours depuis la date d'application du modèle (ex. 0,
/// 30, 60 pour trois versements mensuels à partir d'aujourd'hui).
/// </summary>
public record InstallmentTemplateLine(string Label, decimal Percentage, int OffsetDays);

/// <summary>
/// <paramref name="SkippedCount"/> compte les inscriptions dont le TotalDue est nul (rien à échelonner) —
/// jamais une erreur bloquant tout le lot, comme pour ApplyStandardFeeCommand.
/// </summary>
public record ApplyFeeInstallmentPlanToClassroomResult(int AppliedCount, int SkippedCount);
