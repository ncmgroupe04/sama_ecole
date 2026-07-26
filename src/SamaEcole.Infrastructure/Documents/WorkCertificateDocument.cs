using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetWorkCertificate;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Attestation de travail (A4 portrait) — document RH interne remis à l'employé (banque, ambassade,
/// démarche administrative personnelle). Même charte que le bulletin de paie : en-tête établissement
/// employeur (NINEA), pas de bandeau M.E.N.
/// </summary>
public class WorkCertificateDocument(WorkCertificateDto certificate, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Attestation de travail {certificate.CertificateNumber}",
        Author = certificate.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(24).AlignCenter()
                    .Text("ATTESTATION DE TRAVAIL").Bold().FontSize(16).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {certificate.CertificateNumber}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(28).Element(ComposeBody);
                column.Item().PaddingTop(40).Element(ComposeSignature);
                column.Item().PaddingTop(30).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, certificate.CertificateNumber));
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(certificate.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                var contact = JoinPresent(certificate.SchoolAddress, certificate.SchoolPhone);
                if (contact.Length > 0)
                {
                    header.Item().Text(contact).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(certificate.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {certificate.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeBody(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Text(text =>
            {
                text.DefaultTextStyle(style => style.LineHeight(1.6f));
                text.Justify();
                text.Span("Je soussigné(e), Chef d'Établissement de ");
                text.Span(certificate.SchoolName).Bold();
                if (!string.IsNullOrWhiteSpace(certificate.SchoolAddress))
                {
                    text.Span($" ({certificate.SchoolAddress})");
                }
                text.Span(", certifie que ");
                text.Span(certificate.EmployeeFullName).Bold();
                text.Span(" est employé(e) au sein de notre établissement en qualité de ");
                text.Span(certificate.EmployeeRole).Bold();
                text.Span($", sous contrat de type {ContractTypeLabel()}, depuis le ");
                text.Span(FormatDate(certificate.SinceDate)).Bold();
                text.Span(".");
            });

            column.Item().PaddingTop(14).Text(
                "La présente attestation est délivrée à l'intéressé(e) pour servir et valoir ce que de droit.");
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

    private string ContractTypeLabel() =>
        certificate.ContractType == "Vacataire" ? "Vacataire (horaire)" : "Permanent";

    private string FaitA()
    {
        var date = FormatDate(certificate.IssuedAt);
        return string.IsNullOrWhiteSpace(certificate.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {certificate.SchoolCity}, le {date}";
    }

    private static string JoinPresent(params string?[] parts) =>
        string.Join("  ·  ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string FormatDate(DateTimeOffset moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
