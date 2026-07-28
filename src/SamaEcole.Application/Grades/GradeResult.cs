namespace SamaEcole.Application.Grades;

/// <summary>Résultat commun à CreateGradeCommand et UpdateGradeCommand (ticket JGK-G01).</summary>
public record GradeResult(Guid Id, decimal Value, uint RowVersion);
