using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.RollBook;
using SamaEcole.Application.RollBook.Pdf;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents.Components;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Cahier de roulement imprimable pour l'inspection (Franco-Arabe Bloc 5c) : A4 paysage, une page de grille par mois
/// (autant de pages que nécessaire quand la classe est nombreuse, l'en-tête du tableau se répète), un bandeau
/// officiel (République, Ministère, IA, IEF, établissement) en français et en arabe, la légende, les totaux et taux
/// de la classe, les signatures et le cachet. Une période d'évaluation ajoute le récapitulatif et, pour la direction,
/// la synthèse du niveau.
///
/// Trois langues de rendu (<see cref="RollBookPdfLanguage"/>) : français (gauche à droite), arabe (la page entière de
/// droite à gauche) et bilingue (grille de gauche à droite, libellés français puis arabe). Les blocs arabes sont
/// toujours des blocs RTL complets (<see cref="Bilingual"/>), jamais mêlés au français dans un même texte. Le nom
/// arabe de l'école ou de l'inspection est laissé à compléter à la main (ligne pointillée) quand il n'est pas
/// renseigné : le document n'invente aucune traduction.
///
/// Le document ne calcule rien : toutes les cases, tous les compteurs et tous les taux viennent du modèle, donc des
/// mêmes <see cref="RollBookEngine"/> et <see cref="AttendanceCounts"/> que l'écran.
/// </summary>
public class RollBookDocument(RollBookPdfModel model, RollBookPdfAssets assets) : IDocument
{
    private const string Serif = "Times New Roman";
    private const float PageMargin = 28f;           // ≈ 1 cm
    private const float AvailableWidth = 842f - 2 * PageMargin;
    private const float RankWidth = 14f;
    private const float NameWidth = 112f;
    private const float TotalWidth = 19f;
    private const float RateWidth = 28f;
    private const float MaxCellWidth = 12f;
    private const string Dotted = "..............................";

    private static readonly string LightRed = Colors.Red.Lighten4;
    private static readonly string LightBlue = Colors.Blue.Lighten4;
    private static readonly string LightAmber = Colors.Amber.Lighten4;
    private static readonly string LightYellow = Colors.Yellow.Lighten4;
    private static readonly string LightGrey = Colors.Grey.Lighten3;

    private bool ShowFrench => model.Language != RollBookPdfLanguage.Arabic;
    private bool ShowArabic => model.Language != RollBookPdfLanguage.French;
    private bool Rtl => model.Language == RollBookPdfLanguage.Arabic;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Cahier de roulement {model.ClassroomName} — {PeriodTitleFr()}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        foreach (var month in model.Months)
        {
            ComposePage(container, c => ComposeMonthPage(c, month), PeriodFooter(month));
        }

        if (model.IsTerm && model.TermRows.Count > 0)
        {
            ComposePage(container, ComposeRecapPage, FooterLabel(model.TermLabel!, "Récapitulatif"));
        }

        if (model.LevelClasses.Count > 0)
        {
            ComposePage(container, ComposeLevelPage, FooterLabel(model.TermLabel ?? "", "Synthèse du niveau"));
        }
    }

    // ================================================================================== Page et pied

    private void ComposePage(IDocumentContainer container, Action<IContainer> content, string footer)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(PageMargin);
            page.DefaultTextStyle(text => text.FontFamily(Serif).FontSize(8).FontColor(Colors.Black)
                .DisableFontFeature(FontFeatures.StandardLigatures));

            // Arabe : la page entière se lit de droite à gauche — le tableau place alors le nom de l'élève à droite.
            page.Content().Element(c => (Rtl ? c.ContentFromRightToLeft() : c).Element(content));

            page.Footer().AlignCenter().Element(c => (Rtl ? c.ContentFromRightToLeft() : c).Text(text =>
            {
                if (Rtl)
                {
                    // Le pied d'un document arabe est en arabe : le nom arabe de l'école s'il existe, sinon son nom français.
                    text.DefaultTextStyle(style => style.FontFamily(PdfFonts.Arabic).EnableFontFeature(FontFeatures.StandardLigatures));
                    text.Span($"{(string.IsNullOrWhiteSpace(model.SchoolNameAr) ? model.SchoolName : model.SchoolNameAr)} — {RollBookLabels.TitleAr} — {RollBookLabels.PageAr} ").FontSize(7);
                }
                else
                {
                    text.Span($"{model.SchoolName} — {footer} — page ").FontSize(7);
                }

                text.CurrentPageNumber().FontSize(7);
                text.Span(" / ").FontSize(7);
                text.TotalPages().FontSize(7);
            }));
        });
    }

    private string PeriodFooter(RollBookPdfMonth month) =>
        FooterLabel(RollBookLabels.MonthTitleFr(month.From), "Cahier de roulement");

    private string FooterLabel(string period, string what) => $"{what} {model.ClassroomName} · {period}";

    private string PeriodTitleFr() =>
        model.IsTerm ? model.TermLabel! : RollBookLabels.MonthTitleFr(model.Months[0].From);

    // ================================================================================ Page mensuelle

    private void ComposeMonthPage(IContainer container, RollBookPdfMonth month)
    {
        container.Column(column =>
        {
            column.Item().Element(ComposeBanner);
            column.Item().PaddingTop(3).Element(ComposeEstablishmentBlock);
            column.Item().PaddingTop(5).Element(c => ComposeTitle(c, month));
            column.Item().PaddingTop(5).Element(c => ComposeGrid(c, month));

            // Légende, totaux et signatures restent d'un seul tenant, jamais coupés du tableau qu'ils closent.
            column.Item().PaddingTop(6).ShowEntire().Column(footer =>
            {
                footer.Item().Element(ComposeLegend);
                footer.Item().PaddingTop(5).Element(c => ComposeClassSummary(c, month));
                footer.Item().PaddingTop(8).Element(ComposeSignatures);
            });
        });
    }

    // ----------------------------------------------------------------------------------------- En-tête

    /// <summary>
    /// Bandeau officiel : République, devise, Ministère. En bilingue, le français et l'arabe sont CÔTE À CÔTE (deux demi-
    /// largeurs centrées) plutôt qu'empilés : la page garde la place de son tableau.
    /// </summary>
    private void ComposeBanner(IContainer container)
    {
        void ArabicLines(IContainer c) => c.Column(column =>
        {
            column.Item().AlignCenter().Element(x => Bilingual.ArabicBlock(x, BulletinArabicLabels.Republic, fontSize: 11f, bold: true));
            column.Item().AlignCenter().Element(x => Bilingual.ArabicBlock(x, BulletinArabicLabels.Motto, fontSize: 8.5f));
            column.Item().PaddingTop(2).AlignCenter().Element(x => Bilingual.ArabicBlock(x, BulletinArabicLabels.Ministry, fontSize: 10f, bold: true));
        });

        switch (model.Language)
        {
            case RollBookPdfLanguage.French:
                container.Element(OfficialHeaderComponent.ComposeMinistryBanner);
                break;
            case RollBookPdfLanguage.Arabic:
                container.Element(ArabicLines);
                break;
            default:
                container.Row(row =>
                {
                    row.RelativeItem().Element(OfficialHeaderComponent.ComposeMinistryBanner);
                    row.ConstantItem(12);
                    row.RelativeItem().Element(ArabicLines);
                });
                break;
        }
    }

    /// <summary>
    /// IA / IEF / établissement : les lignes françaises à gauche, leur équivalent arabe à droite (ou une ligne pointillée
    /// à compléter à la main quand la version arabe n'est pas renseignée) ; le logo tout à droite.
    /// </summary>
    private void ComposeEstablishmentBlock(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(4).Row(row =>
        {
            if (ShowFrench)
            {
                row.RelativeItem().Element(c => OfficialHeaderComponent.ComposeEstablishmentLines(
                    c, model.InspectionAcademie, model.InspectionEducationFormation, "Établissement", model.SchoolName));
            }

            if (ShowArabic)
            {
                row.RelativeItem().Column(column =>
                {
                    column.Item().Element(c => ArabicLine(c, RollBookLabels.InspectionAcademieAr, model.InspectionAcademieAr));
                    column.Item().Element(c => ArabicLine(c, RollBookLabels.InspectionEducationAr, model.InspectionEducationFormationAr));
                    column.Item().Element(c => ArabicLine(c, RollBookLabels.SchoolAr, model.SchoolNameAr));
                });
            }

            if (assets.Logo is not null)
            {
                row.ConstantItem(55).MaxHeight(40).AlignRight().Image(assets.Logo).FitArea();
            }
        });
    }

    /// <summary>« libellé : valeur » en arabe ; valeur absente = ligne pointillée, jamais du français translittéré.</summary>
    private static void ArabicLine(IContainer container, string label, string? value) =>
        Bilingual.ArabicBlock(container, $"{label} : {(string.IsNullOrWhiteSpace(value) ? Dotted : value)}", fontSize: 9f);

    private void ComposeTitle(IContainer container, RollBookPdfMonth month)
    {
        container.Column(column =>
        {
            if (ShowFrench)
            {
                column.Item().AlignCenter().Text($"{RollBookLabels.TitleFr} — {RollBookLabels.MonthTitleFr(month.From).ToUpperInvariant()}")
                    .Bold().FontSize(13).Underline();
                column.Item().PaddingTop(1).AlignCenter().Text(FrenchDetail()).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
            }

            if (ShowArabic)
            {
                column.Item().PaddingTop(ShowFrench ? 2 : 0).AlignCenter().Element(c => Bilingual.ArabicBlock(
                    c, $"{RollBookLabels.TitleAr} — {RollBookLabels.MonthWordAr} {RollBookLabels.MonthTitleAr(month.From)}", fontSize: 12f, bold: true));
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, ArabicDetail(), fontSize: 9f));
            }

            if (RollBookLabels.Pattern(model.Pattern) is { } pattern)
            {
                if (ShowFrench)
                {
                    column.Item().PaddingTop(1).AlignCenter().Text(pattern.French).FontSize(8).Italic();
                }

                if (ShowArabic)
                {
                    column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, pattern.Arabic, fontSize: 8f));
                }
            }
        });
    }

    private string FrenchDetail()
    {
        var detail = new List<string> { $"Classe : {model.ClassroomName}" };
        if (!string.IsNullOrWhiteSpace(model.Level))
        {
            detail.Add($"Niveau : {model.Level}");
        }

        detail.Add($"Année scolaire : {model.SchoolYearLabel}");
        if (model.IsTerm)
        {
            detail.Add(model.TermLabel!);
        }

        return string.Join("  ·  ", detail);
    }

    private string ArabicDetail()
    {
        var detail = new List<string> { $"{RollBookLabels.ClassAr} : {model.ClassroomName}" };
        if (RollBookLabels.Level(model.Level) is { } level)
        {
            detail.Add($"{RollBookLabels.LevelAr} : {level}");
        }

        detail.Add($"{RollBookLabels.SchoolYearAr} : {model.SchoolYearLabel.Replace('-', '/')}");
        return string.Join("  ·  ", detail);
    }

    // ---------------------------------------------------------------------------------------- Grille

    private void ComposeGrid(IContainer container, RollBookPdfMonth month)
    {
        var halves = month.ActiveHalves.Count == 0 ? [HalfDay.Morning, HalfDay.Afternoon] : month.ActiveHalves.ToList();
        var offDays = month.Days.Count(d => !d.IsClassDay);
        const float offFactor = 1f;

        // Largeur d'une demi-journée : le jour du mois tient sur la page quel que soit le nombre de colonnes (62 au plus).
        var dayArea = AvailableWidth - RankWidth - NameWidth - 4 * TotalWidth - RateWidth;
        var units = month.ClassDays * halves.Count + offDays * offFactor;
        var cellWidth = units <= 0 ? MaxCellWidth : Math.Min(MaxCellWidth, dayArea / units);

        var marks = month.Students.ToDictionary(
            s => s,
            s => s.Cells.ToDictionary(c => (c.Date, c.Half), c => c.Mark));

        // Absents par colonne (« ligne de totaux par jour ») : justifiées + non justifiées.
        int AbsentsOn(DateOnly date, HalfDay half) =>
            month.Students.Count(s => marks[s].TryGetValue((date, half), out var mark)
                                      && mark is RollBookMark.JustifiedAbsence or RollBookMark.UnjustifiedAbsence);

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(RankWidth);
                columns.ConstantColumn(NameWidth);

                foreach (var day in month.Days)
                {
                    if (day.IsClassDay)
                    {
                        foreach (var _ in halves)
                        {
                            columns.ConstantColumn(cellWidth);
                        }
                    }
                    else
                    {
                        columns.ConstantColumn(cellWidth * offFactor);
                    }
                }

                for (var i = 0; i < 4; i++)
                {
                    columns.ConstantColumn(TotalWidth);
                }

                columns.ConstantColumn(RateWidth);
            });

            // ------------------------------------------------------------------ En-tête sur deux lignes
            table.Header(header =>
            {
                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Text("N°").Bold().FontSize(7);
                header.Cell().RowSpan(2).Element(HeaderCell).AlignMiddle().Element(c => Dual(c, "Nom de l'élève", RollBookLabels.StudentAr, 7.5f));

                foreach (var day in month.Days)
                {
                    var span = day.IsClassDay ? (uint)halves.Count : 1u;
                    var off = !day.IsClassDay;
                    header.Cell().ColumnSpan(span).Element(c => HeaderCell(c, off ? LightGrey : Colors.Grey.Lighten4))
                        .AlignCenter().Column(col =>
                        {
                            col.Item().AlignCenter().Text(day.Date.Day.ToString(CultureInfo.InvariantCulture)).Bold().FontSize(6.5f);
                            col.Item().AlignCenter().Element(c => WeekdayInitial(c, day.Date.DayOfWeek));
                        });
                }

                foreach (var (fr, ar) in new[] { ("P", RollBookLabels.Glyph(RollBookMark.Present, true)), ("R", RollBookLabels.Glyph(RollBookMark.Late, true)),
                                                 ("J", RollBookLabels.Glyph(RollBookMark.JustifiedAbsence, true)), ("A", RollBookLabels.Glyph(RollBookMark.UnjustifiedAbsence, true)) })
                {
                    header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Element(c => Letter(c, fr, ar, 7f, bold: true));
                }

                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Text("%").Bold().FontSize(7);

                // Deuxième ligne : matin / après-midi sous chaque jour de classe (les jours non travaillés n'en ont pas).
                foreach (var day in month.Days)
                {
                    if (!day.IsClassDay)
                    {
                        header.Cell().Element(c => HeaderCell(c, LightGrey));
                        continue;
                    }

                    foreach (var half in halves)
                    {
                        header.Cell().Element(c => HeaderCell(c, Colors.Grey.Lighten4)).AlignCenter()
                            .Element(c => Letter(c, half == HalfDay.Morning ? RollBookLabels.MorningShortFr : RollBookLabels.AfternoonShortFr,
                                half == HalfDay.Morning ? RollBookLabels.MorningShortAr : RollBookLabels.AfternoonShortAr, 5.5f));
                    }
                }
            });

            // ------------------------------------------------------------------------------ Élèves
            var rank = 0;
            foreach (var student in month.Students)
            {
                rank++;
                var studentMarks = marks[student];

                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(rank.ToString(CultureInfo.InvariantCulture)).FontSize(6.5f);
                table.Cell().Element(BodyCell).Column(name => ComposeName(name, student.FullName, student.FullNameAr));

                foreach (var day in month.Days)
                {
                    if (!day.IsClassDay)
                    {
                        // Jour non travaillé : colonne grisée. Une saisie qui s'y trouve reste visible (signalée), sans compter dans les taux.
                        var stray = halves.Select(h => studentMarks.GetValueOrDefault((day.Date, h))).FirstOrDefault(m => m != RollBookMark.NotRecorded);
                        table.Cell().Element(c => BodyCell(c, LightGrey)).AlignCenter().AlignMiddle()
                            .Text(stray == RollBookMark.NotRecorded ? "" : "•").FontSize(6);
                        continue;
                    }

                    foreach (var half in halves)
                    {
                        var mark = studentMarks.GetValueOrDefault((day.Date, half));
                        table.Cell().Element(c => BodyCell(c, MarkBackground(mark))).AlignCenter().AlignMiddle()
                            .Element(c => MarkGlyph(c, mark));
                    }
                }

                var counts = student.Counts;
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(counts.Present.ToString(CultureInfo.InvariantCulture)).FontSize(7);
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(counts.Late.ToString(CultureInfo.InvariantCulture)).FontSize(7);
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(counts.JustifiedAbsence.ToString(CultureInfo.InvariantCulture)).FontSize(7);
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(counts.UnjustifiedAbsence.ToString(CultureInfo.InvariantCulture)).FontSize(7);
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Element(c => PercentText(c, counts.PresenceRate, 6.5f, true));
            }

            // ------------------------------------------------------------- Ligne de totaux par colonne
            table.Cell().ColumnSpan(2).Element(c => BodyCell(c, Colors.Grey.Lighten4)).Element(c => Dual(c, "Absents", RollBookLabels.AbsentsAr, 7f, bold: true));

            foreach (var day in month.Days)
            {
                if (!day.IsClassDay)
                {
                    table.Cell().Element(c => BodyCell(c, LightGrey));
                    continue;
                }

                foreach (var half in halves)
                {
                    var absents = AbsentsOn(day.Date, half);
                    table.Cell().Element(c => BodyCell(c, Colors.Grey.Lighten4)).AlignCenter().AlignMiddle()
                        .Text(absents == 0 ? "" : absents.ToString(CultureInfo.InvariantCulture)).FontSize(6).Bold();
                }
            }

            var totals = month.Totals;
            foreach (var value in new[] { totals.Present, totals.Late, totals.JustifiedAbsence, totals.UnjustifiedAbsence })
            {
                table.Cell().Element(c => BodyCell(c, Colors.Grey.Lighten4)).AlignCenter().AlignMiddle()
                    .Text(value.ToString(CultureInfo.InvariantCulture)).FontSize(7).Bold();
            }

            table.Cell().Element(c => BodyCell(c, Colors.Grey.Lighten4)).AlignCenter().AlignMiddle()
                .Element(c => PercentText(c, totals.PresenceRate, 6.5f, true));
        });
    }

    /// <summary>Un taux : toujours de gauche à droite, même dans une page arabe (sinon « % 80 » au lieu de « 80 % »).</summary>
    private void PercentText(IContainer container, decimal? rate, float size, bool bold)
    {
        var text = (Rtl ? container.ContentFromLeftToRight() : container).Text(RollBookLabels.Percent(rate)).FontSize(size);
        if (bold)
        {
            text.Bold();
        }
    }

    private void ComposeName(ColumnDescriptor name, string frenchName, string? arabicName)
    {
        switch (model.Language)
        {
            case RollBookPdfLanguage.Arabic:
                // Repli sur le français : un élève sans nom arabe doit rester identifiable sur le registre.
                name.Item().Element(c => NameOrFrench(c, frenchName, arabicName));
                break;
            case RollBookPdfLanguage.Bilingual:
                name.Item().Text(frenchName).FontSize(7.5f).Bold();
                name.Item().Element(c => Bilingual.ArabicBlock(c, arabicName, fontSize: 7.5f));
                break;
            default:
                name.Item().Text(frenchName).FontSize(7.5f).Bold();
                break;
        }
    }

    private static void NameOrFrench(IContainer container, string frenchName, string? arabicName)
    {
        if (string.IsNullOrWhiteSpace(arabicName))
        {
            container.Text(frenchName).FontSize(7.5f).Bold();
            return;
        }

        Bilingual.ArabicBlock(container, arabicName, fontSize: 8f, bold: true);
    }

    private void WeekdayInitial(IContainer container, DayOfWeek day)
    {
        if (Rtl)
        {
            Bilingual.ArabicBlock(container, RollBookLabels.WeekdayInitial(day, arabic: true), fontSize: 5.5f);
            return;
        }

        container.Text(RollBookLabels.WeekdayInitial(day, arabic: false)).FontSize(5.5f).FontColor(Colors.Grey.Darken2);
    }

    /// <summary>Une lettre de case : arabe en mode arabe, française sinon (la légende dit les deux en bilingue).</summary>
    private void Letter(IContainer container, string french, string arabic, float size, bool bold = false)
    {
        if (Rtl)
        {
            Bilingual.ArabicBlock(container, arabic, fontSize: size, bold: bold);
            return;
        }

        var text = container.Text(french).FontSize(size);
        if (bold)
        {
            text.Bold();
        }
    }

    private void MarkGlyph(IContainer container, RollBookMark mark)
    {
        var glyph = RollBookLabels.Glyph(mark, Rtl);
        if (glyph.Length == 0)
        {
            return;
        }

        if (Rtl && mark != RollBookMark.PartialAbsence)
        {
            Bilingual.ArabicBlock(container, glyph, fontSize: 7f, bold: true);
            return;
        }

        container.Text(glyph).FontSize(6.5f).Bold();
    }

    private static string MarkBackground(RollBookMark mark) => mark switch
    {
        RollBookMark.UnjustifiedAbsence => LightRed,
        RollBookMark.JustifiedAbsence => LightBlue,
        RollBookMark.Late => LightAmber,
        RollBookMark.PartialAbsence => LightYellow,
        _ => Colors.White
    };

    // ------------------------------------------------------------------------- Légende, totaux, signatures

    private void ComposeLegend(IContainer container)
    {
        container.Row(row =>
        {
            foreach (var mark in new[]
                     {
                         RollBookMark.Present, RollBookMark.Late, RollBookMark.PartialAbsence,
                         RollBookMark.JustifiedAbsence, RollBookMark.UnjustifiedAbsence
                     })
            {
                row.AutoItem().PaddingRight(10).Row(item =>
                {
                    // MinHeight, pas Height : la police arabe a une hauteur de ligne supérieure à 11 pt, un gabarit fixe ferait échouer la mise en page.
                    item.AutoItem().MinWidth(11).MinHeight(11).Background(MarkBackground(mark)).Border(0.4f).BorderColor(Colors.Grey.Medium)
                        .AlignCenter().AlignMiddle().Element(c => MarkGlyph(c, mark));
                    item.AutoItem().PaddingLeft(3).AlignMiddle().Column(label =>
                    {
                        if (ShowFrench)
                        {
                            label.Item().Text(LegendFr(mark)).FontSize(7);
                        }

                        if (ShowArabic)
                        {
                            label.Item().Element(c => Bilingual.ArabicBlock(c, LegendAr(mark), fontSize: 7f));
                        }
                    });
                });
            }

            row.RelativeItem();
        });
    }

    private static string LegendFr(RollBookMark mark) => mark switch
    {
        RollBookMark.Present => RollBookLabels.PresentFr,
        RollBookMark.Late => RollBookLabels.LateFr,
        RollBookMark.PartialAbsence => RollBookLabels.PartialFr,
        RollBookMark.JustifiedAbsence => RollBookLabels.JustifiedFr,
        _ => RollBookLabels.UnjustifiedFr
    };

    private static string LegendAr(RollBookMark mark) => mark switch
    {
        RollBookMark.Present => RollBookLabels.PresentAr,
        RollBookMark.Late => RollBookLabels.LateAr,
        RollBookMark.PartialAbsence => RollBookLabels.PartialAr,
        RollBookMark.JustifiedAbsence => RollBookLabels.JustifiedAr,
        _ => RollBookLabels.UnjustifiedAr
    };

    /// <summary>Totaux et taux de la classe sur la période : les mêmes nombres que l'écran, couverture comprise.</summary>
    private void ComposeClassSummary(IContainer container, RollBookPdfMonth month)
    {
        var totals = month.Totals;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < 6; i++)
                {
                    columns.RelativeColumn();
                }
            });

            SummaryCell(table, month.ExpectedHalfDays.ToString(CultureInfo.InvariantCulture), RollBookLabels.ExpectedFr, RollBookLabels.ExpectedAr);
            SummaryCell(table, totals.Recorded.ToString(CultureInfo.InvariantCulture), RollBookLabels.RecordedFr, RollBookLabels.RecordedAr);
            SummaryCell(table, RollBookLabels.Percent(totals.PresenceRate), RollBookLabels.PresenceRateFr, RollBookLabels.PresenceRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.AbsenceRate), RollBookLabels.AbsenceRateFr, RollBookLabels.AbsenceRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.AssiduityRate), RollBookLabels.AssiduityRateFr, RollBookLabels.AssiduityRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.Coverage(month.ExpectedHalfDays)), RollBookLabels.CoverageFr, RollBookLabels.CoverageAr);
        });
    }

    private void SummaryCell(TableDescriptor table, string value, string french, string arabic)
    {
        table.Cell().Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(3).Column(column =>
        {
            column.Item().AlignCenter().Element(c => (Rtl ? c.ContentFromLeftToRight() : c).Text(value).Bold().FontSize(10));
            if (ShowFrench)
            {
                column.Item().AlignCenter().Text(french).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
            }

            if (ShowArabic)
            {
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 6.5f));
            }
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => SignatureBox(c, RollBookLabels.TeacherFr, RollBookLabels.TeacherAr, null));
            row.ConstantItem(10);
            row.RelativeItem().Element(c => SignatureBox(c, RollBookLabels.SchoolHeadFr, RollBookLabels.SchoolHeadAr, assets.DirectorSignature));
            row.ConstantItem(10);
            row.RelativeItem().Element(c => SignatureBox(c, RollBookLabels.StampFr, RollBookLabels.StampAr, assets.Stamp));
            row.ConstantItem(10);
            row.ConstantItem(150).AlignBottom().Column(column =>
            {
                // Lieu à compléter à la main (aucun lieu en base) ; la date est celle de l'impression.
                var date = model.PrintedOn.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
                if (ShowFrench)
                {
                    column.Item().Text($"Fait à {Dotted[..14]}, le {date}").FontSize(8);
                }

                if (ShowArabic)
                {
                    column.Item().Element(c => Bilingual.ArabicBlock(c, $"حرر في {Dotted[..14]} بتاريخ {date}", fontSize: 8f));
                }
            });
        });
    }

    private void SignatureBox(IContainer container, string french, string arabic, byte[]? image)
    {
        container.Column(column =>
        {
            if (ShowFrench)
            {
                column.Item().AlignCenter().Text(french).Bold().FontSize(8);
            }

            if (ShowArabic)
            {
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 8f, bold: true));
            }

            column.Item().Height(32).AlignCenter().AlignMiddle().Element(c =>
            {
                if (image is not null)
                {
                    c.MaxHeight(32).Image(image).FitArea();
                }
            });
        });
    }

    // ===================================================================== Récapitulatif d'une période

    private void ComposeRecapPage(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(ComposeBanner);
            column.Item().PaddingTop(3).Element(ComposeEstablishmentBlock);
            column.Item().PaddingTop(5).Element(c => ComposeSimpleTitle(c, RollBookLabels.RecapFr, RollBookLabels.RecapAr, model.TermLabel));
            column.Item().PaddingTop(6).Element(ComposeRecapTable);
            column.Item().PaddingTop(6).ShowEntire().Column(footer =>
            {
                footer.Item().Element(c => ComposeRecapTotals(c));
                footer.Item().PaddingTop(8).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeSimpleTitle(IContainer container, string french, string arabic, string? period, bool forClass = true)
    {
        container.Column(column =>
        {
            if (ShowFrench)
            {
                column.Item().AlignCenter().Text($"{french}{(forClass ? $" — {model.ClassroomName}" : "")}{(period is null ? "" : $" — {period}")}")
                    .Bold().FontSize(13).Underline();
                column.Item().PaddingTop(1).AlignCenter().Text(forClass ? FrenchDetail() : $"Année scolaire : {model.SchoolYearLabel}")
                    .FontSize(8.5f).FontColor(Colors.Grey.Darken2);
            }

            if (ShowArabic)
            {
                column.Item().PaddingTop(ShowFrench ? 2 : 0).AlignCenter().Element(c => Bilingual.ArabicBlock(
                    c, forClass ? $"{arabic} — {model.ClassroomName}" : $"{arabic} — {RollBookLabels.Level(model.Level) ?? model.Level}",
                    fontSize: 12f, bold: true));
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(
                    c, forClass ? ArabicDetail() : $"{RollBookLabels.SchoolYearAr} : {model.SchoolYearLabel.Replace('-', '/')}", fontSize: 9f));
            }
        });
    }

    private void ComposeRecapTable(IContainer container)
    {
        var months = model.Months;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(RankWidth);
                columns.RelativeColumn(3f);
                foreach (var _ in months)
                {
                    columns.RelativeColumn(1f);   // absences du mois
                    columns.RelativeColumn(1.1f); // présence du mois
                }

                for (var i = 0; i < 4; i++)
                {
                    columns.RelativeColumn(0.9f);
                }

                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1.2f);
                columns.RelativeColumn(1.2f);
            });

            table.Header(header =>
            {
                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Text("N°").Bold().FontSize(7);
                header.Cell().RowSpan(2).Element(HeaderCell).AlignMiddle().Element(c => Dual(c, "Nom de l'élève", RollBookLabels.StudentAr, 7.5f));

                foreach (var month in months)
                {
                    header.Cell().ColumnSpan(2).Element(HeaderCell).AlignCenter().Element(c =>
                        Dual(c, RollBookLabels.MonthTitleFr(month.From), RollBookLabels.MonthTitleAr(month.From), 7f, center: true));
                }

                foreach (var (fr, ar) in new[] { ("P", "ح"), ("R", "ت"), ("J", "ع"), ("A", "غ") })
                {
                    header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Element(c => Letter(c, fr, ar, 7f, bold: true));
                }

                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Element(c => Dual(c, "Prés.", RollBookLabels.PresenceRateAr, 6.5f, center: true));
                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Element(c => Dual(c, "Assid.", RollBookLabels.AssiduityRateAr, 6.5f, center: true));
                header.Cell().RowSpan(2).Element(HeaderCell).AlignCenter().AlignMiddle().Element(c => Dual(c, "Couv.", RollBookLabels.CoverageAr, 6.5f, center: true));

                foreach (var _ in months)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text("Abs.").FontSize(6);
                    header.Cell().Element(HeaderCell).AlignCenter().Text("%").FontSize(6);
                }
            });

            var expected = model.ExpectedHalfDaysPerStudent;
            var rank = 0;
            foreach (var row in model.TermRows)
            {
                rank++;
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(rank.ToString(CultureInfo.InvariantCulture)).FontSize(6.5f);
                table.Cell().Element(BodyCell).Column(name => ComposeName(name, row.FullName, row.FullNameAr));

                foreach (var month in row.PerMonth)
                {
                    table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(month.Absences.ToString(CultureInfo.InvariantCulture)).FontSize(7);
                    table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Element(c => PercentText(c, month.PresenceRate, 6.5f, false));
                }

                var total = row.Total;
                foreach (var value in new[] { total.Present, total.Late, total.JustifiedAbsence, total.UnjustifiedAbsence })
                {
                    table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Text(value.ToString(CultureInfo.InvariantCulture)).FontSize(7).Bold();
                }

                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Element(c => PercentText(c, total.PresenceRate, 6.5f, true));
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Element(c => PercentText(c, total.AssiduityRate, 6.5f, false));
                table.Cell().Element(BodyCell).AlignCenter().AlignMiddle().Element(c => PercentText(c, total.Coverage(expected), 6.5f, false));
            }
        });
    }

    /// <summary>Totaux de la classe sur toute la période, avec les mêmes quatre taux que la page mensuelle.</summary>
    private void ComposeRecapTotals(IContainer container)
    {
        var totals = model.ClassTotals;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < 6; i++)
                {
                    columns.RelativeColumn();
                }
            });

            SummaryCell(table, model.ClassExpectedHalfDays.ToString(CultureInfo.InvariantCulture), RollBookLabels.ExpectedFr, RollBookLabels.ExpectedAr);
            SummaryCell(table, totals.Recorded.ToString(CultureInfo.InvariantCulture), RollBookLabels.RecordedFr, RollBookLabels.RecordedAr);
            SummaryCell(table, RollBookLabels.Percent(totals.PresenceRate), RollBookLabels.PresenceRateFr, RollBookLabels.PresenceRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.AbsenceRate), RollBookLabels.AbsenceRateFr, RollBookLabels.AbsenceRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.AssiduityRate), RollBookLabels.AssiduityRateFr, RollBookLabels.AssiduityRateAr);
            SummaryCell(table, RollBookLabels.Percent(totals.Coverage(model.ClassExpectedHalfDays)), RollBookLabels.CoverageFr, RollBookLabels.CoverageAr);
        });
    }

    // ===================================================================== Synthèse du niveau

    private void ComposeLevelPage(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(ComposeBanner);
            column.Item().PaddingTop(3).Element(ComposeEstablishmentBlock);
            column.Item().PaddingTop(5).Element(c => ComposeSimpleTitle(c, $"{RollBookLabels.LevelSynthesisFr} {model.Level}", RollBookLabels.LevelSynthesisAr, model.TermLabel, forClass: false));
            column.Item().PaddingTop(8).Element(ComposeLevelTable);
            column.Item().PaddingTop(10).ShowEntire().Element(ComposeSignatures);
        });
    }

    private void ComposeLevelTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.5f);
                for (var i = 0; i < 6; i++)
                {
                    columns.RelativeColumn(1f);
                }
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Element(c => Dual(c, "Classe", RollBookLabels.ClassAr, 8f));
                foreach (var (fr, ar) in new[]
                         {
                             (RollBookLabels.StudentsFr, RollBookLabels.StudentsAr), (RollBookLabels.RecordedFr, RollBookLabels.RecordedAr),
                             (RollBookLabels.PresenceRateFr, RollBookLabels.PresenceRateAr), (RollBookLabels.AbsenceRateFr, RollBookLabels.AbsenceRateAr),
                             (RollBookLabels.AssiduityRateFr, RollBookLabels.AssiduityRateAr), (RollBookLabels.CoverageFr, RollBookLabels.CoverageAr)
                         })
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Element(c => Dual(c, fr, ar, 7f, center: true));
                }
            });

            foreach (var row in model.LevelClasses)
            {
                LevelRow(table, row.Name, row.StudentCount, row.Counts, row.ExpectedHalfDays, bold: false);
            }

            // Niveau : compteurs SOMMÉS puis taux recalculés — jamais une moyenne de taux de classes.
            var total = AttendanceCounts.Sum(model.LevelClasses.Select(c => c.Counts));
            LevelRow(table, $"{RollBookLabels.TotalFr} {model.Level}", model.LevelClasses.Sum(c => c.StudentCount), total,
                model.LevelClasses.Sum(c => c.ExpectedHalfDays), bold: true);
        });

        void LevelRow(TableDescriptor table, string name, int students, AttendanceCounts counts, int expected, bool bold)
        {
            var background = bold ? Colors.Grey.Lighten3 : Colors.White;
            void Cell(string value, bool center = true)
            {
                var text = table.Cell().Element(c => BodyCell(c, background)).AlignMiddle();
                var span = (center ? text.AlignCenter() : text).Element(c => c).Text(value).FontSize(8.5f);
                if (bold)
                {
                    span.Bold();
                }
            }

            Cell(name, center: false);
            Cell(students.ToString(CultureInfo.InvariantCulture));
            Cell(counts.Recorded.ToString(CultureInfo.InvariantCulture));
            Cell(RollBookLabels.Percent(counts.PresenceRate));
            Cell(RollBookLabels.Percent(counts.AbsenceRate));
            Cell(RollBookLabels.Percent(counts.AssiduityRate));
            Cell(RollBookLabels.Percent(counts.Coverage(expected)));
        }
    }

    // ================================================================================== Aides communes

    /// <summary>Libellé français et/ou arabe selon la langue du document ; en bilingue, le français puis l'arabe dessous.</summary>
    private void Dual(IContainer container, string french, string arabic, float size, bool bold = false, bool center = false)
    {
        container.Column(column =>
        {
            if (ShowFrench)
            {
                var line = column.Item();
                var text = (center ? line.AlignCenter() : line).Text(french).FontSize(size);
                if (bold)
                {
                    text.Bold();
                }
            }

            if (ShowArabic)
            {
                var line = column.Item();
                (center ? line.AlignCenter() : line).Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: size, bold: bold));
            }
        });
    }

    private static IContainer HeaderCell(IContainer c) => HeaderCell(c, Colors.Grey.Lighten3);

    private static IContainer HeaderCell(IContainer c, string background) =>
        c.Border(0.5f).BorderColor(Colors.Black).Background(background).PaddingVertical(1.5f).PaddingHorizontal(1);

    private static IContainer BodyCell(IContainer c) => BodyCell(c, Colors.White);

    private static IContainer BodyCell(IContainer c, string background) =>
        c.Border(0.4f).BorderColor(Colors.Grey.Medium).Background(background).MinHeight(11).PaddingVertical(1).PaddingHorizontal(1);
}
