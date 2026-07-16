using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;

namespace SamaEcole.Application.Grades.Commands.SaveGrade;

/// <summary>
/// POST /grades — ticket JGK-G01. Sert À LA FOIS la première saisie et les corrections ultérieures :
/// <see cref="RowVersion"/> absent (null) signifie « je crée une note qui n'existe pas encore » — une
/// création concurrente sur la même clé se heurte alors à l'index unique de Grade, traduite par
/// ApplicationDbContext.SaveChangesAsync en 409, jamais en doublon silencieux. <see cref="RowVersion"/>
/// présent signifie « je corrige la note que j'ai lue avec CE jeton » (verrouillage optimiste xmin,
/// AGENTS.md règle #5, comme UpdateClassFeeCommand).
///
/// IAuditableRequest (JGK-H01) : la saisie de notes fait partie des écritures sensibles explicitement
/// listées par le journal d'audit centralisé.
/// </summary>
public record SaveGradeCommand(
    Guid StudentId,
    Guid SubjectId,
    Guid TermId,
    EvaluationType EvaluationType,
    decimal Value,
    uint? RowVersion)
    : IRequest<SaveGradeResult>, IAuditableRequest;

public record SaveGradeResult(Guid Id, decimal Value, uint RowVersion);
