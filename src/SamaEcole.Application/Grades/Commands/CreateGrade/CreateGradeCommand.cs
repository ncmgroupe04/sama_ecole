using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.CreateGrade;

/// <summary>
/// POST /grades — ticket JGK-G01. Réservé à l'ENSEIGNANT (docs/Volume_7_Security.md « Notes » : Saisir
/// = Enseignant seul, Directeur ✖) — la correction d'une note déjà saisie est une action distincte,
/// UpdateGradeCommand, ouverte aux deux (Volume_7_Security.md « Modifier »).
///
/// Aucun jeton de concurrence à fournir : c'est une création. Une création concurrente sur la même
/// clé (élève/matière/trimestre/type d'évaluation) se heurte à l'index unique de Grade, traduite par
/// ApplicationDbContext.SaveChangesAsync en 409, jamais en doublon silencieux (AGENTS.md règle #5).
///
/// IAuditableRequest (JGK-H01) : la saisie de notes fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé.
/// </summary>
public record CreateGradeCommand(
    Guid StudentId,
    Guid SubjectId,
    Guid TermId,
    EvaluationType EvaluationType,
    decimal Value)
    : IRequest<GradeResult>, IAuditableRequest;
