using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.Institutional;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Rapport de rentrée scolaire IEF (Évolution N°7) — A4 paysage. Trois tableaux, dans l'ordre du canevas :
/// effectifs par classe × tranche d'âge × sexe (statuts et élèves hors norme d'âge), taux de redoublement par
/// niveau, corps professoral (par discipline, par diplôme, liste nominative avec volume horaire). Ne recalcule
/// rien : tout vient de <see cref="IefReportDto"/>, comme l'écran et l'Excel.
/// </summary>
public class IefReportDocument(IefReportDto report) : IDocument
{
    private const string NoValue = "—";
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Rapport de rentrée IEF — {report.SchoolName} — {report.SchoolYearLabel}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.1f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(7.5f).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(6).Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(ComposeClasses);
                column.Item().Element(ComposeRepetition);
                column.Item().Element(ComposeTeachers);
            });
            page.Footer().AlignRight().Text(text =>
            {
                text.Span($"Généré le {report.GeneratedAt.ToString("dd/MM/yyyy HH:mm", French)} — page ");
                text.CurrentPageNumber();
                text.Span(" / ");
                text.TotalPages();
            });
        });
    }

    private void ComposeHeader(IContainer container) =>
        container.BorderBottom(1).PaddingBottom(4).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("RÉPUBLIQUE DU SÉNÉGAL — MINISTÈRE DE L'ÉDUCATION NATIONALE").Bold().FontSize(8.5f);
                left.Item().Text($"IA : {report.InspectionAcademie ?? NoValue}   ·   IEF : {report.InspectionEducationFormation ?? NoValue}");
                left.Item().Text($"{report.SchoolName}   ·   Code : {report.NationalSchoolCode ?? NoValue}").Bold();
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().Text("RAPPORT DE RENTRÉE SCOLAIRE — IEF").Bold().FontSize(11);
                right.Item().Text($"Année scolaire {report.SchoolYearLabel}");
                right.Item().Text($"Âges révolus au {report.AgeReferenceDate.ToString("dd/MM/yyyy", French)}");
            });
        });

    private static IContainer Head(IContainer c) =>
        c.Border(0.5f).Background(Colors.Grey.Lighten3).PaddingVertical(2).PaddingHorizontal(2).AlignCenter();

    private static IContainer Cell(IContainer c) => c.Border(0.5f).PaddingVertical(1.5f).PaddingHorizontal(2);

    private void ComposeClasses(IContainer container) => container.Column(column =>
    {
        column.Item().Text("1. Effectifs par classe, par tranche d'âge et par sexe").Bold().FontSize(9);
        column.Item().PaddingTop(3).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2.2f); // Classe
                cols.RelativeColumn(1.2f); // Norme
                foreach (var _ in report.AgeColumns) { cols.RelativeColumn(0.55f); cols.RelativeColumn(0.55f); }
                for (var i = 0; i < 9; i++) cols.RelativeColumn(0.7f);
            });

            table.Header(header =>
            {
                header.Cell().RowSpan(2).Element(Head).Text("Classe").Bold();
                header.Cell().RowSpan(2).Element(Head).Text("Âge normal").Bold();
                foreach (var bucket in report.AgeColumns) header.Cell().ColumnSpan(2).Element(Head).Text(bucket.Label).Bold();
                foreach (var title in new[] { "F", "G", "Total", "Nouv.", "Redoubl.", "Transf.", "En avance", "En retard", "Âge inconnu" })
                {
                    header.Cell().RowSpan(2).Element(Head).Text(title).Bold();
                }

                foreach (var _ in report.AgeColumns)
                {
                    header.Cell().Element(Head).Text("F");
                    header.Cell().Element(Head).Text("G");
                }
            });

            foreach (var row in report.Classes)
            {
                table.Cell().Element(Cell).Text(row.ClassroomName).Bold();
                table.Cell().Element(Cell).AlignCenter().Text(row.NormLabel ?? NoValue);
                foreach (var cell in row.Cells)
                {
                    table.Cell().Element(Cell).AlignCenter().Text(Count(cell.Girls));
                    table.Cell().Element(Cell).AlignCenter().Text(Count(cell.Boys));
                }

                foreach (var value in new[] { row.Girls, row.Boys, row.Total, row.New, row.Repeaters, row.Transferred, row.Early, row.Late, row.UnknownAge })
                {
                    table.Cell().Element(Cell).AlignCenter().Text(value.ToString(French));
                }
            }

            table.Cell().ColumnSpan(2).Element(Cell).Text("TOTAL").Bold();
            for (var i = 0; i < report.AgeColumns.Count; i++)
            {
                table.Cell().Element(Cell).AlignCenter().Text(report.Classes.Sum(c => c.Cells[i].Girls).ToString(French)).Bold();
                table.Cell().Element(Cell).AlignCenter().Text(report.Classes.Sum(c => c.Cells[i].Boys).ToString(French)).Bold();
            }

            foreach (var value in new[]
                     {
                         report.TotalGirls, report.TotalBoys, report.TotalStudents, report.Classes.Sum(c => c.New),
                         report.TotalRepeaters, report.Classes.Sum(c => c.Transferred), report.Classes.Sum(c => c.Early),
                         report.Classes.Sum(c => c.Late), report.Classes.Sum(c => c.UnknownAge)
                     })
            {
                table.Cell().Element(Cell).AlignCenter().Text(value.ToString(French)).Bold();
            }
        });
    });

    private void ComposeRepetition(IContainer container) => container.Column(column =>
    {
        column.Item().Text("2. Taux de redoublement par niveau").Bold().FontSize(9);
        column.Item().PaddingTop(3).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2);
                for (var i = 0; i < 5; i++) cols.RelativeColumn();
            });
            table.Header(header =>
            {
                foreach (var title in new[] { "Niveau", "Effectif", "Redoublants", "dont Filles", "dont Garçons", "Taux de redoublement" })
                {
                    header.Cell().Element(Head).Text(title).Bold();
                }
            });
            foreach (var row in report.RepetitionByLevel)
            {
                table.Cell().Element(Cell).Text(row.Level).Bold();
                table.Cell().Element(Cell).AlignCenter().Text(row.Enrolled.ToString(French));
                table.Cell().Element(Cell).AlignCenter().Text(row.Repeaters.ToString(French));
                table.Cell().Element(Cell).AlignCenter().Text(row.RepeaterGirls.ToString(French));
                table.Cell().Element(Cell).AlignCenter().Text(row.RepeaterBoys.ToString(French));
                table.Cell().Element(Cell).AlignCenter().Text(Percent(row.RepetitionRate)).Bold();
            }
        });
    });

    private void ComposeTeachers(IContainer container) => container.Column(column =>
    {
        column.Item().Text("3. Corps professoral : discipline, diplôme et volume horaire hebdomadaire").Bold().FontSize(9);
        column.Item().PaddingTop(3).Row(row =>
        {
            row.RelativeItem().Table(table =>
            {
                table.ColumnsDefinition(cols => { cols.RelativeColumn(2.2f); for (var i = 0; i < 4; i++) cols.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var title in new[] { "Discipline", "Enseignants", "Hommes", "Femmes", "Heures / semaine" })
                        h.Cell().Element(Head).Text(title).Bold();
                });
                foreach (var d in report.TeachersByDiscipline)
                {
                    table.Cell().Element(Cell).Text(d.Discipline);
                    table.Cell().Element(Cell).AlignCenter().Text(d.Teachers.ToString(French));
                    table.Cell().Element(Cell).AlignCenter().Text(d.Men.ToString(French));
                    table.Cell().Element(Cell).AlignCenter().Text(d.Women.ToString(French));
                    table.Cell().Element(Cell).AlignCenter().Text(Hours(d.WeeklyHours));
                }
            });

            row.ConstantItem(12);

            row.RelativeItem().Table(table =>
            {
                table.ColumnsDefinition(cols => { cols.RelativeColumn(1.6f); cols.RelativeColumn(1.6f); for (var i = 0; i < 3; i++) cols.RelativeColumn(); });
                table.Header(h =>
                {
                    foreach (var title in new[] { "Diplôme académique", "Diplôme professionnel", "Hommes", "Femmes", "Non renseigné" })
                        h.Cell().Element(Head).Text(title).Bold();
                });
                foreach (var d in report.TeachersByDiploma)
                {
                    table.Cell().Element(Cell).Text(d.Academic.ToString());
                    table.Cell().Element(Cell).Text(d.Professional.ToString());
                    table.Cell().Element(Cell).AlignCenter().Text(d.Men.ToString(French));
                    table.Cell().Element(Cell).AlignCenter().Text(d.Women.ToString(French));
                    table.Cell().Element(Cell).AlignCenter().Text(d.GenderNotReported.ToString(French));
                }
            });
        });

        column.Item().PaddingTop(6).Table(table =>
        {
            table.ColumnsDefinition(cols =>
            {
                cols.RelativeColumn(2.2f); cols.RelativeColumn(0.5f); cols.RelativeColumn(3f);
                cols.RelativeColumn(1.4f); cols.RelativeColumn(1.4f); cols.RelativeColumn(0.9f);
            });
            table.Header(h =>
            {
                foreach (var title in new[] { "Enseignant", "Sexe", "Disciplines", "Diplôme académique", "Diplôme professionnel", "Heures / sem." })
                    h.Cell().Element(Head).Text(title).Bold();
            });
            foreach (var t in report.Teachers)
            {
                table.Cell().Element(Cell).Text(t.FullName);
                table.Cell().Element(Cell).AlignCenter().Text(t.Gender ?? NoValue);
                table.Cell().Element(Cell).Text(t.Disciplines.Count > 0 ? string.Join(", ", t.Disciplines) : NoValue);
                table.Cell().Element(Cell).Text(t.Academic.ToString());
                table.Cell().Element(Cell).Text(t.Professional.ToString());
                table.Cell().Element(Cell).AlignCenter().Text(Hours(t.WeeklyHours));
            }
        });
    });

    private static string Count(int value) => value == 0 ? "" : value.ToString(French);
    private static string Percent(decimal? value) => value is { } v ? $"{v.ToString("0.0", French)} %" : NoValue;
    private static string Hours(decimal value) => value.ToString("0.##", French) + " h";
}
