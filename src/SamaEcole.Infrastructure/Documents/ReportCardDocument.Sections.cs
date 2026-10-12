using System.Globalization;
using SamaEcole.Application.ReportCards;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin FRANCO-ARABE à deux sections (module Franco-Arabe, bloc 3) : matières françaises à gauche, matières
/// arabes à droite, chacune avec sa moyenne et son rang, puis la moyenne générale combinée.
///
/// ÉCART ASSUMÉ avec la référence visuelle (AGENTS.md règle #12), tracé dans docs/design-references/README.md §2 :
/// la référence ne connaît ni section arabe ni bulletin bilingue, et deux tableaux côte à côte ne tiennent pas
/// dans une A5 portrait. Ce gabarit n'est donc retenu QUE si <see cref="ReportCardDto.Sections"/> est renseigné
/// — c'est-à-dire module Coran/Franco-Arabe activé ET au moins une matière arabe pour cet élève. Tout autre
/// bulletin garde sa mise en page A5 d'origine, au caractère près.
///
/// Les blocs communs (en-tête administratif, titre, identité, distinctions, décision du conseil, récapitulatif
/// annuel, pied de page) sont ceux du bulletin ordinaire, réutilisés tels quels : une seule définition de
/// « comment s'imprime une décision du conseil », jamais une seconde copie à garder à jour.
/// </summary>
public partial class ReportCardDocument
{
    /// <summary>Vrai si ce bulletin s'imprime en deux sections (voir remarque de classe).</summary>
    private bool UsesSections => reportCard is { IsBilingualArabic: true, Sections: not null };

    /// <summary>
    /// Compose UNE page A4 paysage. Paysage et non portrait : chaque section reçoit ~140 mm, de quoi porter
    /// matière / coefficient / moyenne / rang / appréciation sans replier les colonnes de chiffres.
    /// </summary>
    internal void ComposeSectionedPage(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(7, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(7.5f).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            // Filigrane : le nom de l'établissement, très pâle, en diagonale. Derrière le contenu (Background) —
            // il ne gêne ni la lecture ni la photocopie en noir et blanc.
            var watermark = Upper(reportCard.SchoolName);
            page.Background().AlignCenter().AlignMiddle().Rotate(-28)
                .Text(watermark).FontSize(Math.Clamp(780f / Math.Max(watermark.Length, 1), 18f, 56f)).Bold().FontColor(Colors.Grey.Lighten4);

            page.Content().Column(column =>
            {
                column.Spacing(3);
                column.Item().Element(ComposeSectionedBanner);
                column.Item().Element(ComposeSectionedAdministrativeHeader);
                column.Item().Element(ComposeTitle);
                if (reportCard.AcceleratedPathLabel is not null)
                {
                    column.Item().Element(ComposeAcceleratedMention);
                }

                column.Item().Element(ComposeIdentity);
                column.Item().PaddingTop(2).Element(ComposeSectionTables);
                column.Item().Element(ComposeSectionSummary);
                if (!IsPrimaire)
                {
                    column.Item().Element(ComposeDisciplinaryMentionsRow);
                }

                // Décision du conseil, récapitulatif annuel et pied de page sur UNE rangée : en paysage la
                // largeur est abondante et la hauteur rare — les empiler comme sur l'A5 ferait déborder.
                column.Item().PaddingTop(3).Row(row =>
                {
                    row.RelativeItem(1.1f).Element(ComposeDecisionDuConseil);
                    row.ConstantItem(8);
                    row.RelativeItem(0.9f).Element(ComposeAnnualRecap);
                    row.ConstantItem(8);
                    row.RelativeItem(2.2f).Element(ComposeFooter);
                });
            });
        });
    }

    /// <summary>
    /// Bandeau officiel : République du Sénégal / Ministère de l'Éducation nationale en français à gauche, leur
    /// équivalent arabe (bloc RTL complet) à droite, logo de l'établissement au centre quand il est fourni.
    /// Chaque côté compose SON bloc — jamais un mélange de directions dans un même texte (voir
    /// <see cref="Bilingual"/>).
    /// </summary>
    private void ComposeSectionedBanner(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(8.5f);
                left.Item().Text("Un Peuple – Un But – Une Foi").Italic().FontSize(7.5f);
                left.Item().Text("Ministère de l'Éducation nationale").FontSize(8f);
            });

            row.ConstantItem(56).AlignCenter().AlignMiddle().Height(34).Element(slot =>
            {
                if (logo is not null)
                {
                    slot.Image(logo).FitArea();
                }
            });

            row.RelativeItem().Column(right =>
            {
                right.Item().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.Republic, fontSize: 9.5f, bold: true));
                right.Item().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.Motto, fontSize: 8f));
                right.Item().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.Ministry, fontSize: 8.5f));
            });
        });
    }

    /// <summary>
    /// « السنة الدراسية : 2026-2027 ». L'année est posée À PART du libellé arabe, en LTR : dans un même bloc RTL,
    /// « 2026-2027 » s'imprimait « 2027-2026 » — un tuteur lisait une année à l'envers. Le libellé reste un bloc
    /// RTL complet, jamais mêlé au latin dans un même texte (voir <see cref="Bilingual"/>).
    /// </summary>
    private void ComposeArabicSchoolYear(IContainer container) =>
        container.Row(row =>
        {
            row.AutoItem().Text(reportCard.SchoolYearLabel).Bold().FontSize(8.5f);
            row.AutoItem().PaddingHorizontal(2).Text(":").Bold().FontSize(8.5f);
            row.AutoItem().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.SchoolYear, fontSize: 8.5f, bold: true));
        });

    /// <summary>
    /// En-tête administratif sur UNE rangée (IA | IEF | établissement | année et période). Le bulletin ordinaire les
    /// empile sur trois lignes ; en paysage la largeur abonde et la hauteur manque, et deux lignes gagnées ici
    /// sont ce qui permet de tenir sur une page avec une section arabe à noms bilingues.
    /// </summary>
    private void ComposeSectionedAdministrativeHeader(IContainer container) =>
        container.Row(row =>
        {
            row.RelativeItem().Element(c => AdministrativeLine(c, "IA", Upper(reportCard.InspectionAcademie)));
            row.RelativeItem().Element(c => AdministrativeLine(c, "IEF", Upper(reportCard.InspectionEducationFormation)));
            row.RelativeItem(1.4f).Element(c => AdministrativeLine(c, reportCard.HeadingPrefix, Upper(reportCard.HeadingName)));
            row.RelativeItem(1.3f).Column(right =>
            {
                right.Item().AlignRight().Text(t =>
                {
                    t.Span("Année Scolaire : ").Bold().FontSize(8.5f);
                    t.Span(reportCard.SchoolYearLabel).FontSize(8.5f);
                    t.Span("  ·  ").FontSize(8.5f);
                    t.Span(reportCard.TermLabel).Bold().FontSize(8.5f);
                });
                right.Item().AlignRight().Element(ComposeArabicSchoolYear);
            });
        });

    private static void AdministrativeLine(IContainer container, string label, string value) =>
        container.Text(text =>
        {
            text.Span($"{label} : ").Bold().FontSize(8.5f);
            text.Span(value).FontSize(8.5f);
        });

    /// <summary>Les deux tableaux de sections, côte à côte ; la section arabe est un bloc RTL complet.</summary>
    private void ComposeSectionTables(IContainer container)
    {
        var sections = reportCard.Sections!;
        var rows = GradeRows();
        var frenchRows = rows.Where(r => ReportSections.Of(r.SubjectId, sections.SubjectSections) == ReportSection.French).ToList();
        var arabicRows = rows.Where(r => ReportSections.Of(r.SubjectId, sections.SubjectSections) == ReportSection.Arabic).ToList();

        // Mêmes interligne et corps des deux côtés, d'après la section la plus chargée : deux tableaux de
        // hauteurs de ligne différentes côte à côte se liraient comme deux documents. Le rembourrage est
        // plafonné — en paysage la hauteur est la ressource rare, un tableau court n'a pas à s'étirer.
        var (fontSize, rowPadding) = RowMetricsFor(Math.Max(frenchRows.Count, arabicRows.Count));
        rowPadding = Math.Min(rowPadding, 1.5f);

        container.DefaultTextStyle(text => text.FontSize(fontSize)).Row(row =>
        {
            row.RelativeItem().Element(c => ComposeSectionTable(c, ReportSection.French, frenchRows, sections.French, rowPadding));
            row.ConstantItem(8);
            row.RelativeItem().Element(c => ComposeSectionTable(c, ReportSection.Arabic, arabicRows, sections.Arabic, rowPadding));
        });
    }

    private void ComposeSectionTable(
        IContainer container, ReportSection section, IReadOnlyList<GradeRow> rows, SectionResultDto result, float rowPadding)
    {
        var isArabic = section == ReportSection.Arabic;
        var showAppreciation = !IsPrimaire;

        // ContentFromRightToLeft sur TOUT le bloc arabe : colonnes du tableau en miroir (la matière à droite),
        // alignement par défaut à droite. Les valeurs latines/numériques se re-posent en LTR cellule par cellule.
        var root = isArabic ? container.ContentFromRightToLeft() : container;

        root.Column(column =>
        {
            column.Item().Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3)
                .PaddingVertical(2).AlignCenter().Column(title =>
                {
                    if (isArabic)
                    {
                        title.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.ArabicSection, fontSize: 9.5f, bold: true));
                        title.Item().AlignCenter().ContentFromLeftToRight().Text("SECTION ARABE").Bold().FontSize(7f);
                    }
                    else
                    {
                        title.Item().AlignCenter().Text("SECTION FRANÇAISE").Bold().FontSize(9f);
                        title.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.FrenchSection, fontSize: 7.5f));
                    }
                });

            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3f);                          // Matière
                    columns.ConstantColumn(NumericColumnWidths.Small);   // Coef
                    columns.ConstantColumn(NumericColumnWidths.Average); // Moyenne
                    columns.ConstantColumn(NumericColumnWidths.Small);   // Rang
                    if (showAppreciation)
                    {
                        columns.RelativeColumn(1.6f);                    // Appréciation
                    }
                });

                table.Header(header =>
                {
                    header.Cell().Element(SectionHeaderCell).Element(c => HeaderLabel(c, "Matière", BulletinArabicLabels.Subject, isArabic, leftAligned: true));
                    header.Cell().Element(SectionHeaderCell).Element(c => HeaderLabel(c, "Coef", BulletinArabicLabels.Coefficient, isArabic));
                    header.Cell().Element(SectionHeaderCell).Element(c => HeaderLabel(c, $"Moy/{reportCard.GradingScale}", BulletinArabicLabels.Average, isArabic));
                    header.Cell().Element(SectionHeaderCell).Element(c => HeaderLabel(c, "Rang", BulletinArabicLabels.Rank, isArabic));
                    if (showAppreciation)
                    {
                        header.Cell().Element(SectionHeaderCell).Element(c => HeaderLabel(c, "Appréciation", BulletinArabicLabels.Appreciation, isArabic, leftAligned: true));
                    }
                });

                foreach (var row in rows)
                {
                    if (row.Graded is { } subject)
                    {
                        table.Cell().Element(SectionBodyCell).Element(c => ComposeSectionSubjectName(c, subject.SubjectId, subject.SubjectName, isArabic));
                        table.Cell().Element(SectionBodyCell).Element(c => NumberCell(c, FormatGrade(subject.Coefficient)));
                        table.Cell().Element(SectionBodyCell).Element(c => NumberCell(c, FormatGrade(subject.Average)));
                        table.Cell().Element(SectionBodyCell).Element(c => NumberCell(c,
                            reportCard.SubjectRanks.GetValueOrDefault(subject.SubjectId, 0) is > 0 and var rank ? rank.ToString(CultureInfo.InvariantCulture) : "—"));
                        if (showAppreciation)
                        {
                            table.Cell().Element(SectionBodyCell).Element(c => ComposeSectionAppreciation(c, subject.SubjectId, isArabic));
                        }

                        continue;
                    }

                    // Matière dispensée : la ligne reste dans SA section, coefficient barré, hors de tout total.
                    var exempt = row.Exempt!;
                    table.Cell().Element(SectionBodyCell).Element(c => ComposeSectionSubjectName(c, exempt.SubjectId, exempt.SubjectName, isArabic));
                    table.Cell().Element(SectionBodyCell).Element(c => c.ContentFromLeftToRight().AlignCenter().Text(FormatGrade(exempt.Coefficient)).Strikethrough());
                    table.Cell().ColumnSpan(showAppreciation ? 3u : 2u).Element(SectionBodyCell).AlignCenter().Element(c =>
                    {
                        if (isArabic)
                        {
                            Bilingual.ArabicBlock(c, BulletinArabicLabels.Exempt);
                        }
                        else
                        {
                            c.Text(ExemptLabel).Italic();
                        }
                    });
                }
            });

            // Totaux de la section : la moyenne de la section est le total des points ÷ le total des coefficients,
            // jamais une moyenne de moyennes — elle se recoupe à la main avec les deux totaux imprimés à côté.
            column.Item().Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(CellPadding)
                .Element(c => ComposeSectionTotals(c, result, isArabic));
        });

        static IContainer SectionHeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(2.5f).PaddingHorizontal(CellPadding);

        IContainer SectionBodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(rowPadding).PaddingHorizontal(CellPadding);

        static void NumberCell(IContainer c, string value) => c.ContentFromLeftToRight().AlignCenter().Text(value);
    }

    /// <summary>En-tête de colonne : français gras (section française) ou arabe gras (section arabe) — jamais les deux, l'espace manque.</summary>
    private static void HeaderLabel(IContainer container, string french, string arabic, bool isArabic, bool leftAligned = false)
    {
        if (isArabic)
        {
            container.AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 8f, bold: true));
            return;
        }

        var aligned = leftAligned ? container : container.AlignCenter();
        aligned.Text(french).Bold();
    }

    /// <summary>
    /// Nom de la matière. Section arabe : le nom arabe (gras, RTL) avec le nom français en petit sur la même ligne ;
    /// sans nom arabe saisi, le français seul — jamais une valeur inventée. Section française : le français seul.
    /// </summary>
    private void ComposeSectionSubjectName(IContainer container, Guid subjectId, string subjectName, bool isArabic)
    {
        var nameAr = reportCard.SubjectNamesAr?.GetValueOrDefault(subjectId);

        if (!isArabic || string.IsNullOrWhiteSpace(nameAr))
        {
            container.ContentFromLeftToRight().Text(subjectName);
            return;
        }

        // Sur UNE seule ligne (arabe à droite, français en petit à gauche) : un nom sur deux lignes doublait la
        // hauteur de chaque ligne de la section arabe, et c'est elle qui fixe la hauteur des deux tableaux.
        container.Row(row =>
        {
            row.RelativeItem().Element(c => Bilingual.ArabicBlock(c, nameAr, fontSize: 9f, bold: true));
            row.AutoItem().AlignMiddle().PaddingRight(3).ContentFromLeftToRight().Text(subjectName).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
        });
    }

    /// <summary>Appréciation de la matière : le libellé français, ou son équivalent arabe dans la section arabe.</summary>
    private void ComposeSectionAppreciation(IContainer container, Guid subjectId, bool isArabic)
    {
        var appreciation = reportCard.SubjectAppreciations.GetValueOrDefault(subjectId);
        if (string.IsNullOrWhiteSpace(appreciation))
        {
            return;
        }

        if (isArabic && BulletinArabicLabels.Appreciations.TryGetValue(appreciation, out var appreciationAr))
        {
            Bilingual.ArabicBlock(container, appreciationAr);
            return;
        }

        container.ContentFromLeftToRight().Text(appreciation);
    }

    private void ComposeSectionTotals(IContainer container, SectionResultDto result, bool isArabic)
    {
        var average = result.Average is { } value ? FormatGrade(value) : "—";
        var rank = result.Rank is { } r ? r.ToString(CultureInfo.InvariantCulture) : "—";

        container.Row(row =>
        {
            Stat(row.RelativeItem(), "Total coef", BulletinArabicLabels.Coefficient, FormatGrade(result.TotalCoefficients));
            Stat(row.RelativeItem(), "Total pts", BulletinArabicLabels.Points, FormatGrade(result.TotalPoints));
            Stat(row.RelativeItem(1.3f), $"Moyenne /{reportCard.GradingScale}", BulletinArabicLabels.Average, average, emphasis: true);
            Stat(row.RelativeItem(), "Rang", BulletinArabicLabels.Rank, rank, emphasis: true);
        });

        void Stat(IContainer cell, string french, string arabic, string value, bool emphasis = false) =>
            cell.AlignCenter().Column(column =>
            {
                if (isArabic)
                {
                    column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 7f));
                }
                else
                {
                    column.Item().AlignCenter().Text(french).FontSize(7f);
                }

                var text = column.Item().AlignCenter().ContentFromLeftToRight().Text(value);
                if (emphasis)
                {
                    text.Bold();
                }
            });
    }

    /// <summary>
    /// Bande de synthèse : moyenne de chaque section, MOYENNE GÉNÉRALE combinée (+ mention) et assiduité. La
    /// moyenne générale est celle du bulletin (<see cref="ReportCardDto.GeneralAverage"/>) — égale, par
    /// construction, à (points français + points arabes) ÷ (coefficients français + arabes).
    /// </summary>
    private void ComposeSectionSummary(IContainer container)
    {
        var sections = reportCard.Sections!;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn(1.3f);
                columns.RelativeColumn(1.1f);
            });

            table.Cell().Element(SummaryCell).Element(c => SummaryBlock(
                c, "Moyenne section française", BulletinArabicLabels.FrenchSectionAverage, SectionLine(sections.French)));
            table.Cell().Element(SummaryCell).Element(c => SummaryBlock(
                c, "Moyenne section arabe", BulletinArabicLabels.ArabicSectionAverage, SectionLine(sections.Arabic)));
            table.Cell().Element(SummaryCell).Background(Colors.Grey.Lighten3).Element(c => SummaryBlock(
                c, "MOYENNE GÉNÉRALE", BulletinArabicLabels.GeneralAverage,
                $"{FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale} — Rang {reportCard.GeneralRank}"
                    + (reportCard.Mention is { Length: > 0 } mention ? $" — {mention}" : ""),
                emphasis: true));
            table.Cell().Element(SummaryCell).Element(c => SummaryBlock(
                c, "Assiduité", BulletinArabicLabels.Attendance,
                $"Abs. {FormatOptionalCount(reportCard.Absences)} · Ret. {FormatOptionalCount(reportCard.Retards)} · Tot. {FormatOptionalCount(reportCard.TotalAbsences)}"));
        });

        string SectionLine(SectionResultDto result) =>
            result.Average is { } average
                ? $"{FormatGrade(average)} /{reportCard.GradingScale} — Rang {result.Rank}"
                : "—";

        static IContainer SummaryCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(CellPadding);

        static void SummaryBlock(IContainer c, string french, string arabic, string value, bool emphasis = false) =>
            c.AlignCenter().Column(column =>
            {
                var label = column.Item().AlignCenter().Text(french).FontSize(7f);
                if (emphasis)
                {
                    label.Bold();
                }

                column.Item().AlignCenter().Element(inner => Bilingual.ArabicBlock(inner, arabic, fontSize: 7.5f, bold: emphasis));
                var text = column.Item().AlignCenter().Text(value).FontSize(emphasis ? 10f : 8.5f);
                if (emphasis)
                {
                    text.Bold();
                }
            });
    }
}
