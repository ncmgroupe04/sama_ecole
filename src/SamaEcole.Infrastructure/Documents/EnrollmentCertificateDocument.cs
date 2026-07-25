using System.Globalization;
using SamaEcole.Application.Enrollments.Queries.GetEnrollmentCertificate;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Certificat de Scolarité officiel (A4 portrait), conforme au formalisme administratif du Ministère
/// de l'Éducation Nationale sénégalais : en-tête République/devise/Ministère, ligne IA/IEF/établissement
/// sur le modèle déjà validé du bulletin (<see cref="SamaEcole.Application.ReportCards.SchoolHeading"/>),
/// formule consacrée « pour servir et valoir ce que de droit ». Remplace l'ancienne attestation A5
/// paysage — même route API, même bouton front, contenu et gabarit refondus.
/// </summary>
public class EnrollmentCertificateDocument(EnrollmentCertificateDto certificate, byte[]? logo) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Certificat de scolarité {certificate.CertificateNumber}",
        Author = certificate.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            // Times New Roman : absente des dépôts Linux (police propriétaire) — le Dockerfile de
            // production mappe ce nom vers Liberation Serif, un clone à métriques identiques, via un
            // alias fontconfig (même convention que ReportCardDocument).
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(11).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeOfficialHeader);
                column.Item().PaddingTop(10).Element(ComposeAdministrativeHeader);
                column.Item().PaddingTop(24).AlignCenter()
                    .Text("CERTIFICAT DE SCOLARITÉ").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {certificate.CertificateNumber}").FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(28).Element(ComposeBody);
                column.Item().PaddingTop(40).Element(ComposeSignature);
            });
        });
    }

    /// <summary>République / devise / Ministère — bandeau centré, identique sur tout certificat officiel.</summary>
    private static void ComposeOfficialHeader(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(12);
            column.Item().AlignCenter().Text("Un Peuple - Un But - Une Foi").Italic().FontSize(9);
            column.Item().PaddingTop(6).AlignCenter().Text("MINISTÈRE DE L'ÉDUCATION NATIONALE").Bold().FontSize(11);
        });
    }

    /// <summary>
    /// IA / IEF / établissement — même trio et même principe (libellé en gras, valeur en normal, ligne
    /// vide si non renseignée plutôt qu'une valeur inventée) que l'en-tête du bulletin.
    /// </summary>
    private void ComposeAdministrativeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Element(c => HeaderLine(c, "Inspection d'Académie de", certificate.InspectionAcademie));
                left.Item().Element(c => HeaderLine(c, "Inspection de l'Éducation et de la Formation de", certificate.InspectionEducationFormation));
                left.Item().Element(c => HeaderLine(c, certificate.HeadingPrefix, certificate.HeadingName));
            });

            if (logo is not null)
            {
                row.ConstantItem(55).MaxHeight(40).AlignRight().Image(logo).FitArea();
            }
        });

        static void HeaderLine(IContainer container, string label, string? value) =>
            container.Text(text =>
            {
                text.Span($"{label} : ").Bold().FontSize(9.5f);
                text.Span(value ?? "").FontSize(9.5f);
            });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            var establishment = certificate.HeadingName ?? certificate.SchoolName;

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
                text.Span($"immatriculé(e) sous le n° {certificate.Matricule}, ");
                text.Span("est régulièrement inscrit(e) dans notre établissement en classe de ");
                text.Span(certificate.ClassroomName).Bold();
                text.Span(", au titre de l'année scolaire ");
                text.Span(certificate.SchoolYearLabel).Bold();
                text.Span(".");
            });

            column.Item().PaddingTop(14).Text(
                "En foi de quoi, le présent certificat lui est délivré pour servir et valoir ce que de droit.");
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
        var date = FormatDate(certificate.EnrolledAt);
        return string.IsNullOrWhiteSpace(certificate.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {certificate.SchoolCity}, le {date}";
    }

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
