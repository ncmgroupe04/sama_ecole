using System.Globalization;
using SamaEcole.Application.Discipline.Queries.GetDisciplinaryPv;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Procès-verbal de sanction disciplinaire (A4 portrait), même charte que les autres documents
/// officiels destinés à la famille (en-tête M.E.N., <see cref="OfficialHeaderComponent"/>). Remis en
/// double exemplaire : un pour le dossier de l'élève, un pour le tuteur.
/// </summary>
public class DisciplinaryPvDocument(DisciplinaryPvDto pv, byte[]? logo, byte[] qrCodeImage) : IDocument
{
    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"PV de discipline {pv.PvNumber}",
        Author = pv.SchoolName
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
                    c, pv.InspectionAcademie, pv.InspectionEducationFormation, pv.HeadingPrefix, pv.HeadingName, logo));

                column.Item().PaddingTop(24).AlignCenter()
                    .Text("PROCÈS-VERBAL DE SANCTION DISCIPLINAIRE").Bold().FontSize(15).Underline();
                column.Item().PaddingTop(2).AlignCenter()
                    .Text($"N° {pv.PvNumber}").FontSize(9).FontColor(Colors.Grey.Darken2);

                column.Item().PaddingTop(24).Element(ComposeIdentityBlock);
                column.Item().PaddingTop(16).Element(ComposeSanctionBlock);
                column.Item().PaddingTop(16).Element(ComposeMotiveBlock);
                column.Item().PaddingTop(16).Element(ComposeGuardianNotice);
                column.Item().PaddingTop(36).Element(ComposeSignatures);
                column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(c, qrCodeImage, pv.PvNumber));
            });
        });
    }

    private void ComposeIdentityBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Column(column =>
        {
            InfoRow(column, "Élève", pv.StudentFullName);
            InfoRow(column, "Matricule", MatriculeText.NoBreak(pv.Matricule));
            InfoRow(column, "Né(e) le", FormatDate(pv.StudentBirthDate));
            InfoRow(column, "Classe", pv.ClassroomName);
            if (!string.IsNullOrWhiteSpace(pv.SchoolYearLabel))
            {
                InfoRow(column, "Année scolaire", pv.SchoolYearLabel);
            }
            InfoRow(column, "Date des faits", pv.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(120).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeSanctionBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("SANCTION PRONONCÉE").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(pv.SanctionType.ToUpperInvariant()).Bold().FontSize(12);
        });
    }

    private void ComposeMotiveBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(column =>
        {
            column.Item().Text("MOTIF").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);
            column.Item().PaddingTop(4).Text(pv.Reason).FontSize(10);
        });
    }

    private void ComposeGuardianNotice(IContainer container)
    {
        container.Text(text =>
        {
            text.DefaultTextStyle(style => style.FontSize(9.5f).Italic().LineHeight(1.4f));
            if (!string.IsNullOrWhiteSpace(pv.GuardianName))
            {
                text.Span("Copie transmise au tuteur : ");
                text.Span(pv.GuardianName).FontColor(Colors.Black).SemiBold();
                if (!string.IsNullOrWhiteSpace(pv.GuardianPhone))
                {
                    text.Span($" ({pv.GuardianPhone})");
                }
                text.Span(".");
            }
            else
            {
                text.Span("Aucun contact de tuteur enregistré pour cet élève à ce jour.");
            }
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().AlignCenter().Text(FaitA()).Italic().FontSize(10);
                left.Item().PaddingTop(4).AlignCenter().Text("Le Chef d'Établissement").FontSize(10);
                left.Item().PaddingTop(30).AlignCenter()
                    .Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
            row.RelativeItem().Column(right =>
            {
                right.Item().AlignCenter().Text("Pris connaissance").FontSize(10);
                right.Item().PaddingTop(4).AlignCenter().Text("Signature du parent / tuteur").FontSize(10);
                right.Item().PaddingTop(30).AlignCenter()
                    .Text("________________________").FontSize(9);
            });
        });
    }

    private string FaitA()
    {
        var date = pv.Date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
        return string.IsNullOrWhiteSpace(pv.SchoolCity)
            ? $"Fait le {date}"
            : $"Fait à {pv.SchoolCity}, le {date}";
    }

    private static string FormatDate(DateOnly moment) =>
        moment.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
