using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Persistence;

/// <summary>
/// Numérotation séquentielle par établissement (docs/Volume_1_Cahier_des_Charges.md §2.2) :
///   - élèves      : ELEV-{année}-{séquence sur 4 chiffres}  — ex. ELEV-2026-0001
///   - enseignants : ENS-{année}-{séquence sur 3 chiffres}   — ex. ENS-2026-001
///
/// Concurrence : le compteur est incrémenté par un unique INSERT ... ON CONFLICT DO UPDATE
/// ... RETURNING. PostgreSQL pose un verrou de ligne sur le compteur : deux inscriptions
/// simultanées dans la même école sont sérialisées, jamais servies avec le même numéro.
///
/// Absence de trou : l'incrément s'exécute dans la transaction ouverte par le Handler
/// (AGENTS.md règle #3). Si l'insertion de l'élève échoue, l'incrément est annulé avec elle.
/// C'est la raison pour laquelle une SEQUENCE PostgreSQL n'est PAS utilisée ici : une séquence
/// ne se rembobine pas en cas de rollback et laisserait un trou dans la numérotation.
/// </summary>
public class MatriculeGenerator(ApplicationDbContext dbContext, TimeProvider timeProvider) : IMatriculeGenerator
{
    public Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Student, prefix: "ELEV", digits: 4, cancellationToken);

    public Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Teacher, prefix: "ENS", digits: 3, cancellationToken);

    private async Task<string> GenerateAsync(
        Guid schoolId,
        MatriculeKind kind,
        string prefix,
        int digits,
        CancellationToken cancellationToken)
    {
        // Année SCOLAIRE (bascule en octobre), pas année civile — voir AcademicYear.
        // Le préfixe et le nombre de chiffres sont figés ici : leur personnalisation par
        // établissement (docs/Volume_1_Cahier_des_Charges.md §2.2, paramètres de l'école)
        // est un ticket distinct — ne pas la bricoler au cas par cas dans les Handlers.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, kind, year, cancellationToken);

        return $"{prefix}-{year}-{next.ToString().PadLeft(digits, '0')}";
    }

    private async Task<int> NextValueAsync(
        Guid schoolId,
        MatriculeKind kind,
        int year,
        CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        var kindText = kind.ToString();
        var now = timeProvider.GetUtcNow();

        // SqlQuery<int> paramètre automatiquement les trous d'interpolation (aucune concaténation SQL).
        // L'alias "Value" est imposé par EF Core pour projeter un scalaire.
        //
        // ToListAsync, et surtout PAS SingleAsync : un INSERT ... RETURNING n'est pas composable,
        // et SingleAsync tenterait de l'envelopper dans un SELECT ... LIMIT — EF Core lève alors
        // « non-composable SQL ». On matérialise donc la ligne unique renvoyée, puis on la valide.
        var values = await dbContext.Database.SqlQuery<int>(
            $"""
             INSERT INTO matricule_sequences ("Id", "SchoolId", "Kind", "Year", "LastValue", "CreatedAt", "IsDeleted")
             VALUES ({id}, {schoolId}, {kindText}, {year}, 1, {now}, FALSE)
             ON CONFLICT ("SchoolId", "Kind", "Year")
             DO UPDATE SET "LastValue" = matricule_sequences."LastValue" + 1,
                           "UpdatedAt" = {now}
             RETURNING "LastValue" AS "Value"
             """)
            .ToListAsync(cancellationToken);

        return values.Single();
    }
}
