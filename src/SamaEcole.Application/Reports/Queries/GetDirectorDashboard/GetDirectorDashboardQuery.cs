using MediatR;

namespace SamaEcole.Application.Reports.Queries.GetDirectorDashboard;

/// <summary>
/// GET /api/v1/reports/dashboard — ticket JGK-R01. État des lieux analytique de l'établissement pour
/// le Directeur, en agrégeant plusieurs modules : effectifs (année active), ressources humaines,
/// assiduité (mois en cours) et abonnement.
///
/// Réservé au Directeur et au Super Admin (voir ReportsController). Les agrégats métier (effectifs,
/// enseignants, présence) passent par le DbContext, donc sous la policy RLS PostgreSQL : deux écoles
/// ne peuvent jamais mélanger leurs chiffres.
/// </summary>
public record GetDirectorDashboardQuery : IRequest<DirectorDashboardDto>;

/// <summary>Effectifs de l'année ACTIVE, ventilés par genre (le genre vit sur l'élève).</summary>
public record EnrollmentStatsDto(int Total, int Boys, int Girls);

/// <summary>
/// Résumé de l'abonnement. <see cref="DaysRemaining"/> peut être négatif (abonnement expiré) ou null
/// (aucune date d'échéance — abonnement en attente de premier paiement).
/// </summary>
public record SubscriptionSummaryDto(string Plan, string Status, DateOnly? ExpiresAt, int? DaysRemaining);

public record NextClassDto(string StartTime, string EndTime, string SubjectName, string TeacherName, string RoomNumber, string ClassroomName);

public record DirectorDashboardDto(
    EnrollmentStatsDto Enrollments,
    int ActiveTeachers,
    /// <summary>Taux (0..1) = (Présents + Retards) / total des lignes d'appel du mois. Null si aucun appel ce mois.</summary>
    decimal? AttendanceRate,
    SubscriptionSummaryDto? Subscription,
    decimal TodayOccupancyRate = 0m,
    IReadOnlyList<NextClassDto>? NextClasses = null);
