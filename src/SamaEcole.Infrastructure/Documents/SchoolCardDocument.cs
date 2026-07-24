using System.Net;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Classrooms.Queries.GetSchoolCardsPdf;

namespace SamaEcole.Infrastructure.Documents;

public class SchoolCardDocument(SchoolCardBatchDto batch) : IDocument
{
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(10, Unit.Millimetre);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(x => x.FontFamily(Fonts.Arial).FontSize(9));

            page.Content().Element(ComposeContent);
        });
    }

    private void ComposeContent(IContainer container)
    {
        // 2 colonnes, 5 rangées par page = 10 cartes par page
        container.Grid(grid =>
        {
            grid.Columns(2);
            grid.HorizontalSpacing(5, Unit.Millimetre);
            grid.VerticalSpacing(1, Unit.Millimetre);

            foreach (var card in batch.Cards)
            {
                grid.Item().Element(c => ComposeCard(c, card));
            }
        });
    }

    private void ComposeCard(IContainer container, SchoolCardDto card)
    {
        // Taille standard carte de crédit CR80 : 85.6mm x 54mm
        container
            .Width(85.6f, Unit.Millimetre)
            .Height(54f, Unit.Millimetre)
            .Decoration(decoration =>
            {
                // Contour (trait de coupe) pour faciliter le découpage
                decoration.Before().Border(1).BorderColor(Colors.Grey.Lighten2);

                decoration.Content().Padding(3, Unit.Millimetre).Column(col =>
                {
                    // En-tête : Nom école
                    col.Item().Row(row =>
                    {
                        if (!string.IsNullOrWhiteSpace(batch.SchoolLogoUrl) && batch.SchoolLogoUrl.StartsWith("http"))
                        {
                            try
                            {
                                using var webClient = new WebClient();
                                var imageBytes = webClient.DownloadData(batch.SchoolLogoUrl);
                                row.AutoItem().Height(10, Unit.Millimetre).Image(imageBytes);
                                row.Spacing(3, Unit.Millimetre);
                            }
                            catch { /* Ignore image error */ }
                        }
                        
                        row.RelativeItem().Column(header =>
                        {
                            header.Item().Text(batch.SchoolName.ToUpper()).Bold().FontSize(10).FontColor(Colors.Blue.Darken2);
                            header.Item().Text($"Année Scolaire {batch.SchoolYearName}").FontSize(7).FontColor(Colors.Grey.Darken1);
                            if (!string.IsNullOrWhiteSpace(batch.PhoneNumber))
                                header.Item().Text($"Tél: {batch.PhoneNumber}").FontSize(6).FontColor(Colors.Grey.Darken1);
                        });
                    });

                    col.Item().PaddingVertical(2, Unit.Millimetre).LineHorizontal(1).LineColor(Colors.Blue.Lighten4);

                    // Titre
                    col.Item().AlignCenter().Text("CARTE D'IDENTITÉ SCOLAIRE").Bold().FontSize(9).FontColor(Colors.Black);
                    col.Item().Height(2, Unit.Millimetre);

                    // Corps (Photo à gauche, détails au centre, QR code à droite)
                    col.Item().Row(row =>
                    {
                        // Emplacement photo
                        row.ConstantItem(15, Unit.Millimetre)
                           .Height(20, Unit.Millimetre)
                           .Border(1)
                           .BorderColor(Colors.Grey.Lighten2)
                           .Background(Colors.Grey.Lighten4)
                           .AlignCenter()
                           .AlignMiddle()
                           .Text("PHOTO")
                           .FontSize(6)
                           .FontColor(Colors.Grey.Medium);

                        row.Spacing(3, Unit.Millimetre);

                        // Infos étudiant
                        row.RelativeItem().Column(info =>
                        {
                            info.Item().Text(text =>
                            {
                                text.Span("Prénoms & Nom : ").SemiBold().FontSize(7);
                                text.Span(card.StudentFullName).Bold().FontSize(8);
                            });
                            info.Item().Text(text =>
                            {
                                text.Span("Né(e) le : ").SemiBold().FontSize(7);
                                text.Span(card.BirthDate.ToString("dd/MM/yyyy")).FontSize(8);
                                if (!string.IsNullOrWhiteSpace(card.BirthPlace))
                                {
                                    text.Span($" à {card.BirthPlace}").FontSize(8);
                                }
                            });
                            info.Item().Text(text =>
                            {
                                text.Span("Classe : ").SemiBold().FontSize(7);
                                text.Span(batch.ClassroomName).Bold().FontSize(8);
                            });
                            info.Item().Text(text =>
                            {
                                text.Span("Matricule : ").SemiBold().FontSize(7);
                                text.Span(card.Matricule).FontSize(8);
                            });
                        });

                        // QR Code à droite
                        row.ConstantItem(15, Unit.Millimetre).AlignRight().Image(card.QrCodeImage);
                    });

                    // Pied de carte
                    col.Item().PaddingTop(2, Unit.Millimetre).AlignRight().Text("Le Directeur").Italic().FontSize(7);
                });
            });
    }

    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;
}
