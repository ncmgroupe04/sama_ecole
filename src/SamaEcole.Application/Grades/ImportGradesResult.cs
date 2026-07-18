namespace SamaEcole.Application.Grades;

/// <summary>Résumé d'un import de notes par fichier CSV/Excel (ImportGradesCommand) — décompte upsert.</summary>
public record ImportGradesResult(int Created, int Updated, int Unchanged);
