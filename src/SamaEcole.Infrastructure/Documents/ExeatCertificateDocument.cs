using System.Globalization;
using SamaEcole.Application.Enrollments.Queries.GetExeatCertificate;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Certificat d'Exéat officiel (A4 portrait), conforme au formalisme M.E.N. sénégalais — atteste
/// qu'une scolarité s'est arrêtée (abandon ou transfert) et l'état du compte de l'élève à cette date.
/// Même charte que <see cref="EnrollmentCertificateDocument"/>, via le composant partagé
/// <see cref="OfficialHeaderComponent"/>.
/// </summary>
public class ExeatCertificateDocument(ExeatCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Certificat d'exéat {certificate.CertificateNumber}",
        Author = certificate.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(11).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(OfficialHeaderComponent.ComposeMinistryBanner);
                column.Item().PaddingTop(10).Element(c => OfficialHeaderComponent.ComposeEstablishmentBlock(
                    c, certificate.InspectionAcademie, certificate.InspectionEducationFormation,
                    certificate.HeadingPrefix, certificate.HeadingName, logo));

                column.Item().PaddingTop(24).AlignCenter()
                    .Text("CERTIFICAT D'EXÉAT").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(certificate.CertificateNumber)}").FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(28).Element(ComposeBody);
                column.Item().PaddingTop(40).Element(ComposeSignature);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, certificate.CertificateNumber));
            });
        });
    }

    private void ComposeBody(IContainer container)
    {
        var establishment = certificate.HeadingName ?? certificate.SchoolName;
        var remaining = certificate.TotalDue - certificate.AmountPaid;

        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.LineHeight(1.6f));
                text.Justify();
                text.Span("Je soussigné(e), Chef d'Établissement de ");
                text.Span(establishment).Bold();
                if (!string.IsNullOrWhiteSpace(certificate.SchoolAddress))
                {
                    text.Span($" ({certificate.SchoolAddress})");
                }
                text.Span(", certifie que l'élève ");
                text.Span(certificate.StudentFullName).Bold();
                text.Span($", né(e) le {FormatDate(certificate.StudentBirthDate)} à {certificate.StudentBirthPlace}, ");
                text.Span($"immatriculé(e) sous le n° {NoBreakText.NoBreak(certificate.Matricule)}, ");
                text.Span("était inscrit(e) dans notre établissement en classe de ");
                text.Span(certificate.ClassroomName).Bold();
                text.Span(", au titre de l'année scolaire ");
                text.Span(certificate.SchoolYearLabel).Bold();
                text.Span(", et a quitté l'établissement le ");
                text.Span(FormatDate(certificate.LeftAt)).Bold();
                text.Span(" au titre de : ");
                text.Span(certificate.Motive).Bold();
                text.Span(".");
            });

            column.Item().PaddingTop(14).Text(remaining <= 0
                ? "L'élève est à jour de ses frais de scolarité à la date de sa sortie."
                : $"Un solde de {FormatMoney(remaining)} restait dû au titre de la scolarité à la date de sortie.");

            column.Item().PaddingTop(14).Text(
                "En foi de quoi, le présent certificat d'exéat lui est délivré pour servir et valoir ce que de droit.");
        });
    }

    private void ComposeSignature(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem();
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text(FaitA()).Italic().FontSize(10);
                right.Item().PaddingTop(4).AlignCenter().Text("Le Chef d'Établissement").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private string FaitA()
    {
        var date = FormatDate(certificate.LeftAt);
        return string.IsNullOrWhiteSpace(certificate.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {certificate.SchoolCity}, le {date}";
    }

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
