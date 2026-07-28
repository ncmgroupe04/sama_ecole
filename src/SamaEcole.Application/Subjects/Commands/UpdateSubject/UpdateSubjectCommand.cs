using MediatR;

namespace SamaEcole.Application.Subjects.Commands.UpdateSubject;

/// <summary>
/// PUT /api/v1/subjects/{id} — corrige le libellé/niveau/coefficient d'une matière déjà créée. Même
/// permission que la création (ticket JGK-G02, GradingPolicies.CanManageGradingScale) : Directeur
/// toujours, Secrétariat seulement si son école a activé la délégation.
///
/// <see cref="RowVersion"/> est le jeton xmin lu à la dernière consultation (SubjectDto.RowVersion) :
/// verrouillage optimiste (AGENTS.md règle #5), même contrat que UpdateGradeCommand. Nommé
/// <c>UpdateSubjectResult</c> (et non <c>SubjectResult</c>, déjà pris par CreateSubjectCommand) pour
/// éviter toute ambiguïté de type dans SubjectsController.
/// </summary>
public record UpdateSubjectCommand(Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion)
    : IRequest<UpdateSubjectResult>;

public record UpdateSubjectResult(Guid Id, string Name, string Level, decimal Coefficient, uint RowVersion);
