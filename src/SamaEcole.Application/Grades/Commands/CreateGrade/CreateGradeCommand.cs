using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.CreateGrade;

/// <summary>
/// POST /grades — ticket JGK-G01. Ouvert au Directeur, au Secrétariat (toutes classes) et à
/// l'Enseignant (docs/Volume_7_Security.md « Notes » : Saisir). La correction d'une note déjà saisie est
/// une action distincte, UpdateGradeCommand, dont l'Enseignant est borné (fenêtre de correction et
/// propriété, voir GradeEditPolicy). L'AUTEUR est consigné dans <c>CreatedBy</c> : c'est lui que cette
/// règle autorise à corriger.
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
