using MediatR;

namespace SamaEcole.Application.Finance.Queries.GetSuggestedPayrollHours;

/// <summary>
/// GET /finance/employee-contracts/{id}/suggested-hours — ticket JGK-K01. Agrège
/// <c>TeacherHourRecord</c> (le registre d'émargement du vacataire, Volume 1 §14.3) et le rapproche
/// de <c>ScheduleSlot</c> (l'emploi du temps planifié) pour PRÉ-REMPLIR le champ <c>HoursWorked</c>
/// de <c>GenerateFichePaieCommand</c> côté client.
///
/// N'écrit RIEN et ne modifie en rien <c>GenerateFichePaieCommand</c> : c'est une suggestion
/// consultative, la Direction reste seule à valider (ou corriger) le nombre d'heures avant de
/// soumettre la commande existante — arbitrage explicite, Volume 1 §14.3 amendé. Un écart signalé ici
/// n'empêche jamais la génération de la fiche de paie.
/// </summary>
public record GetSuggestedPayrollHoursQuery(Guid EmployeeContractId, int Month, int Year) : IRequest<SuggestedPayrollHoursDto>;

public record SuggestedPayrollHoursDto(
    Guid EmployeeContractId,
    decimal SuggestedHours,
    IReadOnlyList<PayrollHoursDiscrepancyDto> Discrepancies);

/// <summary>
/// Écart, pour un jour donné, entre les heures DÉCLARÉES (TeacherHourRecord) et les heures
/// PLANIFIÉES (ScheduleSlot du même jour de la semaine). Purement informatif — voir la classe.
/// </summary>
public record PayrollHoursDiscrepancyDto(DateOnly Date, decimal DeclaredHours, decimal ScheduledHours);
