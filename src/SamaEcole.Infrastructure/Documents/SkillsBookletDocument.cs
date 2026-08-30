using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Livret de compétences — A4 portrait, PLUSIEURS pages admises (Volume 1 §23.6, ticket JGK-M07).
///
/// Portrait et multi-pages, contrairement au bulletin et au STATEDUC : le livret est une LISTE longue
/// (40 à 60 compétences) avec peu de colonnes (2 ou 3 trimestres). C'est la hauteur qui manque, jamais
/// la largeur. Comprimer une grille APC complète sur une page la rendrait illisible pour la famille,
/// qui est sa première destinataire.
///
/// Le nombre de colonnes de périodes est VARIABLE — deux semestres ou trois trimestres selon l'école —
/// et lu sur le modèle. Rien n'est codé en dur : une école qui passerait au semestre obtiendrait
/// sinon un livret à trois colonnes dont une vide.
///
/// UNE CASE VIDE SIGNIFIE « NON ÉVALUÉE », et rien d'autre. Imprimer « NA » (non acquis) à la place
/// porterait un jugement que personne n'a formulé — sur le document qui suit l'élève d'école en école.
/// </summary>
public class SkillsBookletDocument(SkillsBookletModel model) : IDocument
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Livret de compétences — {model.StudentFullName}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.4f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Column(column =>
            {
                column.Item().Element(ComposeIdentity);
                column.Item().PaddingTop(10).Element(ComposeSkillsTable);
                column.Item().PaddingTop(10).Element(ComposeLegend);
                column.Item().PaddingTop(10).Element(ComposeAppreciation);
                column.Item().PaddingTop(12).Element(ComposeSignature);
            });
            page.Footer().Element(ComposePageNumber);
        });
    }

    private void ComposeHeader(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(5).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    if (!string.IsNullOrWhiteSpace(model.InspectionAcademie))
                    {
                        left.Item().Text($"IA : {model.InspectionAcademie.ToUpperInvariant()}").FontSize(8);
                    }

                    if (!string.IsNullOrWhiteSpace(model.InspectionEducationFormation))
                    {
                        left.Item().Text($"IEF : {model.InspectionEducationFormation.ToUpperInvariant()}").FontSize(8);
                    }

                    // Même règle que le bulletin : la ligne s'imprime réduite à son préfixe de cycle
                    // quand l'école n'a pas renseigné le nom — jamais un repli sur la raison sociale.
                    left.Item().Text($"{model.HeadingPrefix} : {(model.HeadingName ?? "").ToUpperInvariant()}")
                        .Bold().FontSize(8);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text("LIVRET DE COMPÉTENCES").Bold().FontSize(13);
                    right.Item().AlignRight()
                        .Text($"Année scolaire {model.SchoolYearLabel}").FontSize(9);
                });
            });
        });

    private void ComposeIdentity(IContainer container) =>
        container.Border(0.75f).BorderColor(Colors.Black).Padding(6).Row(row =>
        {
            row.RelativeItem(2).Column(c =>
            {
                c.Item().Text(model.StudentFullName).Bold().FontSize(12);
                c.Item().Text($"Matricule : {model.Matricule}").FontSize(8.5f);

                // L'IEN figure au livret parce que c'est la pièce qui SUIT l'élève : c'est par lui
                // que l'école d'accueil rapprochera ce livret du dossier national.
                if (model.IenNumber is { Length: > 0 } ien)
                {
                    c.Item().Text($"IEN : {ien}").FontSize(8.5f);
                }
            });

            row.RelativeItem().Column(c =>
            {
                c.Item().Text($"Né(e) le : {model.BirthDate.ToString("dd/MM/yyyy", FrenchCulture)}").FontSize(8.5f);
                c.Item().Text($"À : {model.BirthPlace}").FontSize(8.5f);
            });

            row.RelativeItem().Column(c =>
            {
                c.Item().Text($"Classe : {model.ClassroomName}").Bold().FontSize(8.5f);
                c.Item().Text($"Niveau : {model.Level}").FontSize(8.5f);
            });
        });

    private void ComposeSkillsTable(IContainer container) =>
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.4f); // Domaine
                columns.RelativeColumn(4.2f); // Compétence

                // Une colonne par période, quelle qu'en soit le nombre — voir la remarque de classe.
                foreach (var _ in model.TermLabels)
                {
                    columns.RelativeColumn(1.1f);
                }
            });

            // En-tête RÉPÉTÉ en tête de chaque page : sur un document multi-pages, une grille dont la
            // deuxième page n'a plus d'en-tête est illisible — le lecteur ne sait plus quelle colonne
            // est quel trimestre.
            table.Header(header =>
            {
                HeaderCell(header, "Domaine", left: true);
                HeaderCell(header, "Compétence", left: true);

                foreach (var term in model.TermLabels)
                {
                    HeaderCell(header, term);
                }
            });

            foreach (var domain in model.Domains)
            {
                for (var i = 0; i < domain.Competencies.Count; i++)
                {
                    var competency = domain.Competencies[i];

                    // Le nom du domaine, une seule fois, fusionné sur la hauteur de ses compétences.
                    // RowSpan CALCULÉ, jamais deviné — même mécanique que le bulletin APC.
                    if (i == 0)
                    {
                        table.Cell().RowSpan((uint)domain.Competencies.Count)
                            .Border(0.75f).BorderColor(Colors.Black)
                            .PaddingVertical(3).PaddingHorizontal(4)
                            .AlignMiddle().Text(domain.Name).Bold().FontSize(8.5f);
                    }

                    table.Cell().Element(BodyCell).Text(competency.Label).FontSize(8.5f);

                    // On parcourt les PÉRIODES, pas la liste des niveaux : si la grille en portait
                    // moins que de périodes (données partielles), les cases manquantes s'impriment
                    // vides au lieu de faire dérailler le tableau d'une colonne.
                    for (var period = 0; period < model.TermLabels.Count; period++)
                    {
                        var level = period < competency.Levels.Count ? competency.Levels[period] : null;

                        table.Cell().Element(BodyCell).AlignCenter()
                            .Text(SkillAcquisition.Abbreviate(level)).Bold().FontSize(8.5f);
                    }
                }
            }
        });

    /// <summary>
    /// Légende des abréviations. OBLIGATOIRE : « ECA » ne veut rien dire pour un parent, et le livret
    /// est d'abord destiné à la famille. Un tableau de sigles sans légende n'informe personne.
    /// </summary>
    private static void ComposeLegend(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(5).Row(row =>
        {
            foreach (var level in Enum.GetValues<SkillAcquisitionLevel>())
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span($"{SkillAcquisition.Abbreviate(level)} ").Bold().FontSize(8);
                    text.Span(SkillAcquisition.Describe(level)).FontSize(8);
                });
            }

            row.RelativeItem().Text(text =>
            {
                text.Span("(vide) ").Bold().FontSize(8);
                text.Span("Non évaluée sur la période").Italic().FontSize(8);
            });
        });

    private void ComposeAppreciation(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text("Appréciation générale du conseil").Bold().FontSize(9);

            // Le cadre s'imprime même vide (MinHeight) : c'est l'emplacement où le conseil écrira à la
            // main si rien n'a été saisi. Jamais une phrase générée à sa place.
            column.Item().PaddingTop(3).Border(0.5f).BorderColor(Colors.Black)
                .MinHeight(52).Padding(5)
                .Text(model.OverallAppreciation ?? "").FontSize(9.5f);
        });

    private void ComposeSignature(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Signature du tuteur").Bold().FontSize(8.5f);
                left.Item().PaddingTop(2).Border(0.5f).BorderColor(Colors.Grey.Medium).MinHeight(40);
            });

            row.ConstantItem(20);

            row.RelativeItem().AlignRight().Column(right =>
            {
                if (model.DirectorSignature is not null)
                {
                    right.Item().AlignRight().Height(26).Image(model.DirectorSignature).FitArea();
                }

                right.Item().AlignRight().Text("Le Chef d'Établissement").Bold().FontSize(8.5f);

                if (model.OfficialStamp is not null)
                {
                    right.Item().PaddingTop(3).AlignRight().Height(48).Width(48)
                        .Image(model.OfficialStamp).FitArea();
                }
            });
        });

    private static void ComposePageNumber(IContainer container) =>
        container.AlignCenter().Text(text =>
        {
            text.Span("Page ").FontSize(7).FontColor(Colors.Grey.Darken2);
            text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken2);
            text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Darken2);
            text.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken2);
        });

    private static void HeaderCell(TableCellDescriptor header, string label, bool left = false)
    {
        var cell = header.Cell()
            .Border(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3)
            .PaddingVertical(3).PaddingHorizontal(4);

        (left ? cell : cell.AlignCenter()).Text(label).Bold().FontSize(8);
    }

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Medium).PaddingVertical(2.5f).PaddingHorizontal(4);
}
