using System.Globalization;
using System.IO.Compression;
using System.Text;
using SamaEcole.Application.Common.Interfaces;
using SamaEcole.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace SamaEcole.Application.SchoolYears.Queries.ExportSchoolYear;

public class ExportSchoolYearQueryHandler(IApplicationDbContext dbContext, TimeProvider timeProvider)
    : IRequestHandler<ExportSchoolYearQuery, SchoolYearExportResult>
{
    private static readonly IReadOnlyDictionary<PaymentMethod, string> MethodLabels = new Dictionary<PaymentMethod, string>
    {
        [PaymentMethod.Cash] = "Espèces",
        [PaymentMethod.Cheque] = "Chèque",
        [PaymentMethod.Transfer] = "Virement",
        [PaymentMethod.MobileMoney] = "Mobile Money"
    };

    private record EnrolledStudentRow(Guid EnrollmentId, string Matricule, string FullName, DateOnly BirthDate, string Gender, Guid ClassroomId, string ClassroomName, string ClassroomLevel, string? GuardianName, string? GuardianPhone);

    public async Task<SchoolYearExportResult> Handle(ExportSchoolYearQuery request, CancellationToken cancellationToken)
    {
        // Le Global Query Filter + la policy RLS cantonnent d'office la lecture à l'école du JWT.
        var schoolYear = await dbContext.SchoolYears.AsNoTracking()
            .FirstOrDefaultAsync(y => y.Id == request.SchoolYearId, cancellationToken)
            ?? throw new KeyNotFoundException($"Année scolaire {request.SchoolYearId} introuvable.");

        // Inscriptions NON ANNULÉES de CETTE année : le pivot des deux autres fichiers. Exporter « les
        // élèves de l'école » serait faux — on veut ceux inscrits CETTE année-là (règle #6 : une
        // inscription annulée n'a jamais existé pour l'archive, elle n'a jamais été confirmée).
        var enrolled = await (
            from e in dbContext.Enrollments.AsNoTracking()
            join s in dbContext.Students.AsNoTracking() on e.StudentId equals s.Id
            join c in dbContext.Classrooms.AsNoTracking() on e.ClassroomId equals c.Id
            where e.SchoolYearId == request.SchoolYearId && e.Status != EnrollmentStatus.Cancelled
            select new EnrolledStudentRow(
                e.Id, s.Matricule, s.FullName, s.BirthDate, s.Gender,
                c.Id, c.Name, c.Level, s.GuardianName, s.GuardianPhone))
            .ToListAsync(cancellationToken);

        // L'archive hors plateforme garde la TRACE (règle #6) : les paiements ANNULÉS y figurent, marqués, avec leur motif
        // et la destination de l'argent — y compris ceux d'une inscription annulée ou archivée depuis (suppression
        // guidée), dont l'élève peut lui-même avoir été supprimé. IgnoreQueryFilters retire AUSSI le filtre de tenant :
        // le SchoolId est réaffirmé à chaque requête (la policy RLS reste la seconde barrière).
        var yearEnrollments = await (
            from e in dbContext.Enrollments.IgnoreQueryFilters().AsNoTracking()
            join s in dbContext.Students.IgnoreQueryFilters().AsNoTracking() on e.StudentId equals s.Id
            where e.SchoolId == schoolYear.SchoolId && s.SchoolId == schoolYear.SchoolId && e.SchoolYearId == request.SchoolYearId
            select new PaymentOwnerRow(e.Id, s.Matricule, s.FullName, s.IsDeleted))
            .ToListAsync(cancellationToken);

        var paymentOwners = yearEnrollments.ToDictionary(x => x.EnrollmentId);
        var paymentEnrollmentIds = paymentOwners.Keys.ToList();

        var payments = await dbContext.Payments.AsNoTracking()
            .Where(p => paymentEnrollmentIds.Contains(p.EnrollmentId))
            .OrderBy(p => p.PaidAt)
            .ToListAsync(cancellationToken);

        var studentsCsv = BuildStudentsCsv(enrolled);
        var paymentsCsv = BuildPaymentsCsv(payments, paymentOwners);
        var classroomsCsv = BuildClassroomsCsv(enrolled);

        var zipBytes = BuildZip(
            ("eleves.csv", studentsCsv),
            ("paiements.csv", paymentsCsv),
            ("classes.csv", classroomsCsv));

        var fileName = $"Export-{Slugify(schoolYear.Label)}-{timeProvider.GetUtcNow():yyyyMMdd}.zip";

        return new SchoolYearExportResult(zipBytes, fileName);
    }

    private static string BuildStudentsCsv(IReadOnlyList<EnrolledStudentRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvLine("Matricule", "Nom complet", "Date de naissance", "Genre", "Classe", "Niveau", "Nom du tuteur", "Téléphone du tuteur"));

        foreach (var row in rows.OrderBy(r => r.FullName, StringComparer.Ordinal))
        {
            sb.AppendLine(CsvLine(
                row.Matricule,
                row.FullName,
                row.BirthDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                row.Gender,
                row.ClassroomName,
                row.ClassroomLevel,
                row.GuardianName,
                row.GuardianPhone));
        }

        return sb.ToString();
    }

    private static readonly IReadOnlyDictionary<PaymentCancellationOutcome, string> OutcomeLabels =
        new Dictionary<PaymentCancellationOutcome, string>
        {
            [PaymentCancellationOutcome.KeptBySchool] = "Simple correction",
            [PaymentCancellationOutcome.ReusedForEnrollment] = "Réutilisé pour l'inscription",
            [PaymentCancellationOutcome.RefundedToParent] = "Remboursé au parent"
        };

    /// <summary>Propriétaire d'un paiement dans l'archive : l'élève de l'inscription, même supprimé depuis.</summary>
    private sealed record PaymentOwnerRow(Guid EnrollmentId, string Matricule, string FullName, bool StudentDeleted);

    private static string BuildPaymentsCsv(
        IReadOnlyList<Domain.Entities.Payment> payments, IReadOnlyDictionary<Guid, PaymentOwnerRow> owners)
    {
        var sb = new StringBuilder();
        // Les colonnes d'origine restent en tête et dans le même ordre : les colonnes ajoutées (statut, annulation) sont à la suite.
        sb.AppendLine(CsvLine(
            "Reçu N°", "Matricule", "Élève", "Montant (FCFA)", "Mode", "Date",
            "Statut", "Annulé le", "Motif d'annulation", "Destination de l'argent"));

        foreach (var payment in payments)
        {
            // Toujours résolu : la liste des paiements est construite à partir des inscriptions de l'année.
            // Une entrée absente signalerait une incohérence de données, pas un cas normal à absorber.
            var owner = owners[payment.EnrollmentId];
            var cancelled = payment.Status == PaymentStatus.Cancelled;

            sb.AppendLine(CsvLine(
                payment.ReceiptNumber,
                owner.Matricule,
                owner.StudentDeleted ? $"{owner.FullName} (élève supprimé)" : owner.FullName,
                payment.Amount.ToString(CultureInfo.InvariantCulture),
                MethodLabels.GetValueOrDefault(payment.Method, payment.Method.ToString()),
                payment.PaidAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                cancelled ? "Annulé" : "Valide",
                cancelled && payment.CancelledAt is { } at ? at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : "",
                cancelled ? payment.CancellationReason : "",
                cancelled && payment.CancellationOutcome is { } outcome ? OutcomeLabels.GetValueOrDefault(outcome, outcome.ToString()) : ""));
        }

        return sb.ToString();
    }

    private static string BuildClassroomsCsv(IReadOnlyList<EnrolledStudentRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CsvLine("Nom", "Niveau", "Effectif (année)"));

        var byClassroom = rows
            .GroupBy(r => new { r.ClassroomId, r.ClassroomName, r.ClassroomLevel })
            .OrderBy(g => g.Key.ClassroomName, StringComparer.Ordinal);

        foreach (var group in byClassroom)
        {
            sb.AppendLine(CsvLine(group.Key.ClassroomName, group.Key.ClassroomLevel, group.Count().ToString(CultureInfo.InvariantCulture)));
        }

        return sb.ToString();
    }

    /// <summary>Échappement RFC 4180 minimal : virgule, guillemet ou retour à la ligne impose des guillemets englobants.</summary>
    private static string CsvCell(string? value)
    {
        value ??= string.Empty;

        return value.IndexOfAny([',', '"', '\n', '\r']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string CsvLine(params string?[] cells) => string.Join(",", cells.Select(CsvCell));

    /// <summary>ZIP en mémoire, BOM UTF-8 sur chaque CSV pour que les caractères accentués s'affichent correctement dans Excel.</summary>
    private static byte[] BuildZip(params (string EntryName, string Content)[] files)
    {
        using var memoryStream = new MemoryStream();

        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (entryName, content) in files)
            {
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
                using var entryStream = entry.Open();
                using var writer = new StreamWriter(entryStream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                writer.Write(content);
            }
        }

        return memoryStream.ToArray();
    }

    /// <summary>« 2026-2027 » → « 2026-2027 » (déjà sans espace) ; retire tout caractère qu'un nom de fichier n'accepte pas partout.</summary>
    private static string Slugify(string label)
    {
        var chars = label.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray();
        return new string(chars);
    }
}
