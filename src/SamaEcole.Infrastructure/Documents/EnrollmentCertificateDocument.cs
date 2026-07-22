using System.Globalization;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Certificat / Attestation d'Inscription officiel en PDF (Axe 2). Purement académique et administratif,
/// sans tableau de prix ni mentions financières (réservées au reçu financier de la Caisse / Finance).
/// </summary>
public class EnrollmentCertificateDocument(EnrollmentCertificateDto certificate, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Certificat d'inscription {certificate.CertificateNumber}",
        Author = certificate.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A5.Landscape());
            page.Margin(8, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(8).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                ComposeHeader(column);

                column.Item().PaddingTop(6).AlignCenter()
                    .Text($"CERTIFICAT D'INSCRIPTION n° {certificate.CertificateNumber}").Bold().Italic().FontSize(11);

                column.Item().PaddingTop(6).Row(row =>
                {
                    row.RelativeItem().Element(ComposeInfoBlock);
                    row.ConstantItem(14);
                    row.RelativeItem().Element(ComposeAttestationBlock);
                });

                column.Item().PaddingTop(12).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(ColumnDescriptor column)
    {
        column.Item().BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(certificate.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(certificate.SchoolAddress, certificate.SchoolPhone, certificate.SchoolEmail);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                var legal = JoinPresent(
                    certificate.SchoolNinea is null ? null : $"NINEA : {certificate.SchoolNinea}",
                    certificate.SchoolRegistreCommerce is null ? null : $"RCCM : {certificate.SchoolRegistreCommerce}");
                if (legal.Length > 0)
                {
                    header.Item().Text(legal).FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).Image(logo).FitArea();
            }
            else
            {
                row.ConstantItem(60).AlignRight().AlignMiddle()
                    .Text("[Logo officiel]").FontSize(7).FontColor(Colors.Grey.Medium);
            }
        });
    }

    private void ComposeInfoBlock(IContainer container)
    {
        container.Column(column =>
        {
            InfoRow(column, "Matricule", certificate.Matricule);
            InfoRow(column, "Nom complet", certificate.StudentFullName);
            var dob = FormatDate(certificate.StudentBirthDate);
            var place = string.IsNullOrWhiteSpace(certificate.StudentBirthPlace) ? "" : $" à {certificate.StudentBirthPlace}";
            InfoRow(column, "Né(e) le", $"{dob}{place}");
            InfoRow(column, "Sexe", certificate.StudentGender == "Male" ? "Masculin" : "Féminin");
            InfoRow(column, "Classe d'affectation", $"{certificate.ClassroomName} — {certificate.ClassroomLevel}");
            InfoRow(column, "Année scolaire", certificate.SchoolYearLabel);
            InfoRow(column, "Type de mouvement", TypeLabel(certificate.Type));
            InfoRow(column, "Date d'inscription", FormatDate(certificate.EnrolledAt));
            if (!string.IsNullOrWhiteSpace(certificate.GuardianName))
            {
                InfoRow(column, "Tuteur", certificate.GuardianName);
            }
            if (!string.IsNullOrWhiteSpace(certificate.GuardianPhone))
            {
                InfoRow(column, "Contact tuteur", certificate.GuardianPhone);
            }
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value)
    {
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(95).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });
    }

    private void ComposeAttestationBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Background(Colors.Grey.Lighten4).Padding(8).Column(column =>
        {
            column.Item().Text("ATTESTATION OFFICIELLE").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(6).Text(text =>
            {
                text.DefaultTextStyle(style => style.FontSize(8).LineHeight(1.4f));
                text.Span("Le Directeur / Le Secrétariat de l'établissement ");
                text.Span(certificate.SchoolName).Bold();
                text.Span(" atteste que l'élève ");
                text.Span($"{certificate.StudentFullName} (Matricule : {certificate.Matricule}) ").Bold();
                text.Span("est régulièrement inscrit(e) en classe de ");
                text.Span(certificate.ClassroomName).Bold();
                text.Span(" pour le compte de l'année scolaire ");
                text.Span(certificate.SchoolYearLabel).Bold();
                text.Span(".\n\nEn foi de quoi, la présente attestation lui est délivrée pour servir et valoir ce que de droit.");
            });
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(FaitA()).Italic();
                left.Item().PaddingTop(6).Text("[Cadre Cachet Officiel]").FontSize(7).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Text("Signature du Directeur / Secrétariat").Italic();
        });
    }

    private string FaitA()
    {
        var date = FormatDate(certificate.EnrolledAt);
        return string.IsNullOrWhiteSpace(certificate.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {certificate.SchoolCity}, le {date}";
    }

    private static string TypeLabel(string type) =>
        type == "ReEnrollment" ? "Réinscription" : "Nouvelle inscription";

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
