using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Common;
using SamaEcole.Domain.Entities;
using SamaEcole.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Persistence;

/// <summary>
/// Numérotation séquentielle par établissement (docs/Volume_1_Cahier_des_Charges.md §2.2).
///
/// Le GABARIT vient des paramètres de l'école (ticket JGK-B02 : studentMatriculeFormat /
/// teacherMatriculeFormat), il n'est plus codé en dur. Une école qui n'a pas encore touché à ses
/// réglages retombe sur les valeurs par défaut — « ELEV-{YEAR}-{SEQ:4} », « ENS-{YEAR}-{SEQ:3} ».
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
    /// <summary>
    /// Gabarit FIXE des numéros de reçu (ticket JGK-E02), contrairement aux matricules dont le format
    /// est paramétrable par l'école : un reçu est une pièce officielle, sa forme ne se règle pas. Rendu
    /// par le même moteur que les matricules — « REC-2025-0002 ».
    /// </summary>
    private const string ReceiptNumberFormat = "REC-{YEAR}-{SEQ:4}";

    /// <summary>Gabarit FIXE des certificats de mutation (ticket JGK-M06) — même raison que le reçu.</summary>
    private const string MutationCertificateNumberFormat = "MUT-{YEAR}-{SEQ:4}";

    public Task<string> GenerateNextStudentMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Student, cancellationToken);

    public Task<string> GenerateNextTeacherMatriculeAsync(Guid schoolId, CancellationToken cancellationToken) =>
        GenerateAsync(schoolId, MatriculeKind.Teacher, cancellationToken);

    public async Task<string> GenerateNextReceiptNumberAsync(Guid schoolId, CancellationToken cancellationToken)
    {
        // Même année SCOLAIRE que le matricule généré au même instant dans la même transaction : le reçu
        // et le matricule d'une inscription portent donc toujours le même millésime.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, MatriculeKind.Receipt, year, cancellationToken);

        return MatriculeFormat.Render(ReceiptNumberFormat, year, next);
    }

    public async Task<string> GenerateNextMutationCertificateNumberAsync(
        Guid schoolId, CancellationToken cancellationToken)
    {
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, MatriculeKind.MutationCertificate, year, cancellationToken);

        return MatriculeFormat.Render(MutationCertificateNumberFormat, year, next);
    }

    /// <summary>
    /// Prochaine valeur du compteur d'IEN provisoires de l'école — exposée à
    /// <see cref="NationalIenGenerator"/>, qui compose le numéro final avec le code établissement.
    /// La séquence vit ici, et non dans le générateur d'IEN, pour hériter telle quelle de la
    /// sérialisation sous concurrence et de l'annulation sur rollback décrites en tête de classe.
    /// </summary>
    internal Task<int> NextProvisionalIenSequenceAsync(
        Guid schoolId, int year, CancellationToken cancellationToken) =>
        NextValueAsync(schoolId, MatriculeKind.ProvisionalIen, year, cancellationToken);

    private async Task<string> GenerateAsync(
        Guid schoolId,
        MatriculeKind kind,
        CancellationToken cancellationToken)
    {
        var format = await ResolveFormatAsync(schoolId, kind, cancellationToken);

        // Année SCOLAIRE (bascule en octobre), pas année civile — voir AcademicYear.
        var year = AcademicYear.ForDate(timeProvider.GetUtcNow());
        var next = await NextValueAsync(schoolId, kind, year, cancellationToken);

        return MatriculeFormat.Render(format, year, next);
    }

    private async Task<string> ResolveFormatAsync(
        Guid schoolId,
        MatriculeKind kind,
        CancellationToken cancellationToken)
    {
        // IgnoreQueryFilters + filtre explicite : le générateur est appelé dans la transaction
        // d'inscription, où le tenant EST celui de l'école — mais aussi par le provisionnement, où
        // le filtre global ne serait pas satisfait. On filtre donc soi-même, sans se reposer dessus.
        var settings = await dbContext.SchoolSettings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SchoolId == schoolId && !s.IsDeleted, cancellationToken);

        // Aucun réglage enregistré (école antérieure à JGK-B02) : on retombe sur les valeurs par
        // défaut plutôt que d'échouer. Une inscription ne doit pas dépendre d'un écran de réglages
        // que personne n'a encore ouvert.
        return kind switch
        {
            MatriculeKind.Student => settings?.StudentMatriculeFormat ?? SchoolSettingsDefaults.StudentMatriculeFormat,
            MatriculeKind.Teacher => settings?.TeacherMatriculeFormat ?? SchoolSettingsDefaults.TeacherMatriculeFormat,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Type de matricule inconnu.")
        };
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
