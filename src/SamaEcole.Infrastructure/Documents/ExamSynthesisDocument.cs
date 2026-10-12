using System.Globalization;
using SamaEcole.Application.Exams;
using SamaEcole.Domain.Enums;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Traductions arabes des libellés FIXES des documents d'examen (module Franco-Arabe, bloc 4). Même réserve que
/// <see cref="BulletinArabicLabels"/> : ⚠️ À FAIRE RELIRE PAR UNE PERSONNE LISANT L'ARABE avant mise en production —
/// notamment l'intitulé exact des trois diplômes tel que l'écrit l'administration.
/// </summary>
public static class ExamArabicLabels
{
    public const string FrancoArabic = "فرنسي عربي";
    public const string SessionPv = "محضر الدورة";
    public const string CandidateSheet = "بطاقة تلخيصية للمترشح";
    public const string SchoolYear = "السنة الدراسية";
    public const string Candidate = "المترشح";
    public const string Class = "القسم";
    public const string TableNumber = "رقم الطاولة";
    public const string Center = "مركز الامتحان";
    public const string NationalId = "المعرف الوطني للتلميذ";
    public const string FrenchAverage = "معدل القسم الفرنسي";
    public const string ArabicAverage = "معدل القسم العربي";
    public const string GeneralAverage = "المعدل العام";
    public const string Result = "النتيجة";
    public const string Mention = "التقدير";
    public const string ExamAverage = "معدل الامتحان";
    public const string Admitted = "ناجح(ة)";
    public const string NotAdmitted = "راسب(ة)";
    public const string SchoolHead = "مدير المؤسسة";
    public const string BirthDate = "تاريخ الميلاد";
    public const string BirthPlace = "مكان الميلاد";
    public const string Registration = "رقم التسجيل";

    public static string ExamType(Domain.Enums.ExamType type) => type switch
    {
        Domain.Enums.ExamType.CFEE => "شهادة ختم الدروس الابتدائية",
        Domain.Enums.ExamType.BFEM => "شهادة ختم الدروس المتوسطة",
        _ => "شهادة البكالوريا"
    };

    public static string MentionLabel(ExamMention mention) => mention switch
    {
        ExamMention.Passable => "مقبول",
        ExamMention.AssezBien => "حسن",
        ExamMention.Bien => "جيد",
        _ => "جيد جدا"
    };
}

/// <summary>
/// PV de session et fiche synthétique d'un candidat à un examen officiel (CFEE, BFEM, BAC), bilingues français /
/// arabe quand l'école active le module Coran/Franco-Arabe. Deux gabarits, UN modèle
/// (<see cref="ExamSynthesisModel"/>) : le PV est un tableau A4 paysage, la fiche une page A4 portrait par candidat.
///
/// Ce que les documents ne font PAS : ils ne transmettent rien au SIMEN (aucune API publique, Volume 1 §23) et
/// n'inventent aucun identifiant — l'IEN s'imprime tel qu'il est en base, ou « — » s'il n'a pas été attribué.
/// Les arabes sont des blocs RTL complets (<see cref="Bilingual"/>), jamais mêlés au français dans un même texte.
/// </summary>
public class ExamSynthesisDocument(ExamSynthesisModel model, byte[]? logo, bool sessionPv) : IDocument
{
    private const float RuleThickness = 0.75f;

    public DocumentMetadata GetMetadata() => new()
    {
        Title = sessionPv ? $"PV {model.Reference}" : $"Fiche synthétique {model.Candidates.FirstOrDefault()?.Reference}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        if (sessionPv)
        {
            ComposePv(container);
            return;
        }

        foreach (var candidate in model.Candidates)
        {
            ComposeSheet(container, candidate);
        }
    }

    // ================================================================== PV de session (A4 paysage)

    private void ComposePv(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.5f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(9).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            // L'en-tête officiel n'est imprimé qu'UNE fois, sur la première page : répété, il mangeait la moitié de
            // chaque page et ne laissait de la place qu'à cinq candidats. Les pages suivantes se repèrent à
            // l'en-tête du tableau (Table.Header), répété, et au pied de page.
            page.Content().Column(column =>
            {
                column.Item().Element(ComposeBanner);
                column.Item().PaddingTop(4).Element(c => OfficialHeaderComponent.ComposeEstablishmentBlock(
                    c, model.InspectionAcademie, model.InspectionEducationFormation, "Établissement", model.SchoolName, logo));
                column.Item().PaddingTop(6).Element(c => ComposeTitle(c, "PROCÈS-VERBAL DE SESSION", ExamArabicLabels.SessionPv));
                column.Item().PaddingTop(8).Element(ComposePvTable);

                // Signature APRÈS le tableau, jamais coupée de lui : ShowEntire garde le bloc d'un seul tenant.
                column.Item().PaddingTop(10).ShowEntire().Element(ComposeSignature);
            });

            page.Footer().AlignCenter().Text(text =>
            {
                text.Span($"{model.SchoolName} — {model.Reference} — page ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" / ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    }

    private void ComposePvTable(IContainer container)
    {
        var bilingual = model.IsBilingualArabic;

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(22);   // N°
                columns.ConstantColumn(58);   // N° de table
                columns.ConstantColumn(88);   // IEN
                columns.RelativeColumn(3);    // Candidat
                columns.RelativeColumn(1);    // Classe
                if (bilingual)
                {
                    columns.ConstantColumn(46); // Moy. française
                    columns.ConstantColumn(46); // Moy. arabe
                }

                columns.ConstantColumn(46);   // Moy. générale
                columns.RelativeColumn(1.3f); // Résultat
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).AlignCenter().Text("N°").Bold();
                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "N° de table", ExamArabicLabels.TableNumber));
                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "IEN", ExamArabicLabels.NationalId));
                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Candidat", ExamArabicLabels.Candidate, left: true));
                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Classe", ExamArabicLabels.Class));
                if (bilingual)
                {
                    header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Moy. FR", ExamArabicLabels.FrenchAverage));
                    header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Moy. AR", ExamArabicLabels.ArabicAverage));
                }

                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Moy. gén.", ExamArabicLabels.GeneralAverage));
                header.Cell().Element(HeaderCell).Element(c => HeaderLabel(c, "Résultat", ExamArabicLabels.Result));
            });

            var rank = 0;
            foreach (var candidate in model.Candidates)
            {
                rank++;
                table.Cell().Element(BodyCell).AlignCenter().Text(rank.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).AlignCenter().Text(candidate.CandidateNumber ?? "—");
                table.Cell().Element(BodyCell).AlignCenter().Text(candidate.IenNumber ?? "—");
                table.Cell().Element(BodyCell).Column(name =>
                {
                    name.Item().Text(candidate.StudentFullName).Bold();
                    name.Item().Element(c => Bilingual.ArabicBlock(c, candidate.StudentFullNameAr, fontSize: 9f));
                });
                table.Cell().Element(BodyCell).AlignCenter().Text(candidate.ClassroomName);
                if (bilingual)
                {
                    table.Cell().Element(BodyCell).AlignCenter().Text(Format(candidate.FrenchAverage));
                    table.Cell().Element(BodyCell).AlignCenter().Text(Format(candidate.ArabicAverage));
                }

                table.Cell().Element(BodyCell).AlignCenter().Text(Format(candidate.CombinedAverage)).Bold();
                table.Cell().Element(BodyCell).Element(c => ComposeResult(c, candidate, compact: true));
            }
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2);
        static IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(3);
    }

    // ================================================================== Fiche synthétique (A4 portrait)

    private void ComposeSheet(IDocumentContainer container, ExamSynthesisCandidate candidate)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(2, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(11).FontColor(Colors.Black).DisableFontFeature(FontFeatures.StandardLigatures));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeBanner);
                column.Item().PaddingTop(8).Element(c => OfficialHeaderComponent.ComposeEstablishmentBlock(
                    c, model.InspectionAcademie, model.InspectionEducationFormation, "Établissement", model.SchoolName, logo));
                column.Item().PaddingTop(12).Element(c => ComposeTitle(c, "FICHE SYNTHÉTIQUE DU CANDIDAT", ExamArabicLabels.CandidateSheet));

                column.Item().PaddingTop(10).Element(c => ComposeCandidateBlock(c, candidate));
                column.Item().PaddingTop(10).Element(c => ComposeExamBlock(c, candidate));
                column.Item().PaddingTop(10).Element(c => ComposeAveragesBlock(c, candidate));
                column.Item().PaddingTop(10).Element(c => ComposeResultBlock(c, candidate));
                column.Item().PaddingTop(14).Element(ComposeSignature);
            });
        });
    }

    private void ComposeCandidateBlock(IContainer container, ExamSynthesisCandidate candidate)
    {
        container.Border(RuleThickness).BorderColor(Colors.Black).Padding(8).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text(candidate.StudentFullName).Bold().FontSize(14);
                if (!string.IsNullOrWhiteSpace(candidate.StudentFullNameAr))
                {
                    row.RelativeItem().Element(c => Bilingual.ArabicBlock(c, candidate.StudentFullNameAr, fontSize: 14f, bold: true));
                }
            });

            column.Item().PaddingTop(6).Element(c => LabelledRow(c, "Né(e) le", candidate.BirthDate == default ? "—" : candidate.BirthDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), ExamArabicLabels.BirthDate));
            column.Item().Element(c => LabelledRow(c, "à", string.IsNullOrWhiteSpace(candidate.BirthPlace) ? "—" : candidate.BirthPlace, ExamArabicLabels.BirthPlace));
            column.Item().Element(c => LabelledRow(c, "Classe", candidate.ClassroomName, ExamArabicLabels.Class));
            column.Item().Element(c => LabelledRow(c, "Matricule", candidate.Matricule, ExamArabicLabels.Registration));

            // L'IEN s'imprime tel quel, ou « — » quand l'administration ne l'a pas attribué : jamais inventé.
            column.Item().Element(c => LabelledRow(c, "IEN (identifiant national)", candidate.IenNumber ?? "— (non attribué)", ExamArabicLabels.NationalId));
        });
    }

    private void ComposeExamBlock(IContainer container, ExamSynthesisCandidate candidate)
    {
        container.Border(RuleThickness).BorderColor(Colors.Black).Padding(8).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text($"{ExamTitle()}").Bold().FontSize(12);
                if (model.IsBilingualArabic)
                {
                    row.RelativeItem().Element(c => Bilingual.ArabicBlock(c, ExamArabicLabels.ExamType(model.ExamType), fontSize: 12f, bold: true));
                }
            });

            column.Item().PaddingTop(4).Element(c => LabelledRow(c, "Session", model.SchoolYearLabel, ExamArabicLabels.SchoolYear));
            if (!string.IsNullOrWhiteSpace(model.Series))
            {
                column.Item().Element(c => LabelledRow(c, "Série", model.Series!, null));
            }

            column.Item().Element(c => LabelledRow(c, "N° de table", candidate.CandidateNumber ?? "— (non attribué)", ExamArabicLabels.TableNumber));
            column.Item().Element(c => LabelledRow(c, "Centre d'examen", candidate.ExamCenterName ?? "— (non attribué)", ExamArabicLabels.Center));
        });
    }

    private void ComposeAveragesBlock(IContainer container, ExamSynthesisCandidate candidate)
    {
        container.Row(row =>
        {
            if (model.IsBilingualArabic)
            {
                row.RelativeItem().Element(c => AverageBox(c, "Moyenne section française", ExamArabicLabels.FrenchAverage, candidate.FrenchAverage, candidate.GradingScale));
                row.ConstantItem(8);
                row.RelativeItem().Element(c => AverageBox(c, "Moyenne section arabe", ExamArabicLabels.ArabicAverage, candidate.ArabicAverage, candidate.GradingScale));
                row.ConstantItem(8);
            }

            row.RelativeItem().Element(c => AverageBox(c, "Moyenne générale de l'année", ExamArabicLabels.GeneralAverage, candidate.CombinedAverage, candidate.GradingScale, emphasis: true));
        });

        void AverageBox(IContainer box, string french, string arabic, decimal? value, int scale, bool emphasis = false)
        {
            var styled = box.Border(RuleThickness).BorderColor(Colors.Black).Padding(6);
            if (emphasis)
            {
                styled = styled.Background(Colors.Grey.Lighten3);
            }

            styled.AlignCenter().Column(column =>
            {
                column.Item().AlignCenter().Text(french).FontSize(9);
                if (model.IsBilingualArabic)
                {
                    column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 9f));
                }

                column.Item().PaddingTop(2).AlignCenter().Text(value is null ? "—" : $"{Format(value)} /{scale}").Bold().FontSize(emphasis ? 15 : 13);
            });
        }
    }

    private void ComposeResultBlock(IContainer container, ExamSynthesisCandidate candidate)
    {
        container.Border(RuleThickness).BorderColor(Colors.Black).Padding(8).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text("Résultat de la délibération").Bold();
                if (model.IsBilingualArabic)
                {
                    row.RelativeItem().Element(c => Bilingual.ArabicBlock(c, ExamArabicLabels.Result, fontSize: 11f, bold: true));
                }
            });

            column.Item().PaddingTop(4).Element(c => ComposeResult(c, candidate, compact: false));
        });
    }

    /// <summary>
    /// Résultat : « — » tant que le jury n'a rien saisi (jamais « non admis » par défaut). Le CFEE n'attribue pas de
    /// mention (Volume 1 §22.6) : aucune n'est imprimée pour lui, même saisie par erreur.
    /// </summary>
    private void ComposeResult(IContainer container, ExamSynthesisCandidate candidate, bool compact)
    {
        if (candidate.IsAdmitted is not { } admitted)
        {
            container.Text("—");
            return;
        }

        var mention = model.ExamType == ExamType.CFEE ? null : candidate.Mention;

        container.Column(column =>
        {
            var french = admitted ? "Admis(e)" : "Non admis(e)";
            if (mention is { } m)
            {
                french += $" — {MentionFrench(m)}";
            }

            column.Item().Text(french).Bold();

            if (model.IsBilingualArabic)
            {
                var arabic = admitted ? ExamArabicLabels.Admitted : ExamArabicLabels.NotAdmitted;
                if (compact)
                {
                    // Une seule ligne arabe dans le tableau du PV : la hauteur d'une ligne fixe le nombre de candidats par page.
                    var line = mention is { } mc ? $"{arabic} — {ExamArabicLabels.MentionLabel(mc)}" : arabic;
                    column.Item().Element(c => Bilingual.ArabicBlock(c, line, fontSize: 8.5f, bold: true));
                }
                else
                {
                    column.Item().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 11f, bold: true));
                    if (mention is { } m2)
                    {
                        column.Item().Element(c => Bilingual.ArabicBlock(c, $"{ExamArabicLabels.Mention}: {ExamArabicLabels.MentionLabel(m2)}", fontSize: 10f));
                    }
                }
            }

            if (!compact && candidate.ExamAverage is { } average)
            {
                column.Item().PaddingTop(2).Text($"Moyenne à l'examen : {Format(average)}");
                if (model.IsBilingualArabic)
                {
                    column.Item().Element(c => Bilingual.ArabicBlock(c, ExamArabicLabels.ExamAverage, fontSize: 9f));
                }
            }

            if (!compact && candidate.DeliberatedOn is { } on)
            {
                column.Item().Text($"Délibération du {on.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}").FontSize(9);
            }
        });
    }

    // ================================================================== Éléments communs

    /// <summary>Bandeau officiel : République / devise / Ministère en français, leur équivalent arabe en dessous si bilingue.</summary>
    private void ComposeBanner(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(OfficialHeaderComponent.ComposeMinistryBanner);
            if (model.IsBilingualArabic)
            {
                column.Item().PaddingTop(2).AlignCenter().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.Republic, fontSize: 11f, bold: true));
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, BulletinArabicLabels.Ministry, fontSize: 10f, bold: true));
            }
        });
    }

    private void ComposeTitle(IContainer container, string frenchTitle, string arabicTitle)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text($"{frenchTitle} — {ExamTitle().ToUpperInvariant()}").Bold().FontSize(14).Underline();
            if (model.IsBilingualArabic)
            {
                column.Item().PaddingTop(2).AlignCenter().Element(c => Bilingual.ArabicBlock(c, $"{arabicTitle} — {ExamArabicLabels.ExamType(model.ExamType)}", fontSize: 12f, bold: true));
            }

            var detail = new List<string> { $"Année scolaire {model.SchoolYearLabel}" };
            if (!string.IsNullOrWhiteSpace(model.Series))
            {
                detail.Add($"Série {model.Series}");
            }

            column.Item().PaddingTop(2).AlignCenter().Text(string.Join(" · ", detail)).FontSize(9).FontColor(Colors.Grey.Darken2);
        });
    }

    /// <summary>« CFEE », « BFEM » ou « BAC », suivi de « Franco-Arabe » pour une école du module — un seul libellé partout.</summary>
    private string ExamTitle() => model.IsBilingualArabic ? $"{model.ExamType} Franco-Arabe" : model.ExamType.ToString();

    private void ComposeSignature(IContainer container)
    {
        container.AlignRight().Column(column =>
        {
            column.Item().AlignCenter().Text("Le Chef d'Établissement").Bold();
            if (model.IsBilingualArabic)
            {
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, ExamArabicLabels.SchoolHead, fontSize: 9f, bold: true));
            }

            column.Item().Height(30);
        });
    }

    private void LabelledRow(IContainer container, string french, string value, string? arabic)
    {
        container.PaddingVertical(1.5f).Row(row =>
        {
            row.RelativeItem(1.4f).Text(text =>
            {
                text.Span($"{french} : ").Bold().FontSize(10);
                text.Span(value).FontSize(10.5f);
            });

            if (model.IsBilingualArabic && arabic is not null)
            {
                row.RelativeItem(1).Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 9f));
            }
        });
    }

    private void HeaderLabel(IContainer container, string french, string arabic, bool left = false)
    {
        container.Column(column =>
        {
            var line = column.Item();
            (left ? line : line.AlignCenter()).Text(french).Bold().FontSize(8.5f);
            if (model.IsBilingualArabic)
            {
                column.Item().AlignCenter().Element(c => Bilingual.ArabicBlock(c, arabic, fontSize: 7.5f, bold: true));
            }
        });
    }

    private static string MentionFrench(ExamMention mention) => mention switch
    {
        ExamMention.Passable => "Passable",
        ExamMention.AssezBien => "Assez Bien",
        ExamMention.Bien => "Bien",
        _ => "Très Bien"
    };

    /// <summary>« — » quand il n'y a pas de moyenne (aucune note dans la section) : jamais un zéro qui vaudrait échec.</summary>
    internal static string Format(decimal? value) => value is { } v ? ReportCardDocument.FormatGrade(v) : "—";
}
