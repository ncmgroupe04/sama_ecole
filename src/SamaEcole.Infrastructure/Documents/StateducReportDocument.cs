using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.StateIntegration;
using SamaEcole.Domain.Enums;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Rapport annuel STATEDUC — A4 PAYSAGE (Volume 1 §23.3, ticket JGK-M04).
///
/// Le paysage est imposé par le contenu, pas choisi : le tableau des effectifs porte neuf colonnes et
/// celui des qualifications croise deux nomenclatures. En portrait, les libellés de niveau et de
/// diplôme se replient sur deux lignes et l'agent qui ressaisit le formulaire à l'IEF perd sa ligne.
///
/// PLUSIEURS PAGES SONT ADMISES, contrairement au bulletin : une école à 14 niveaux et 40 enseignants
/// ne tient pas sur une page, et comprimer le corps jusqu'à ce qu'elle y tienne rendrait le document
/// illisible. Chaque tableau porte son en-tête répété en tête de page (QuestPDF <c>Header</c>).
///
/// AUCUNE CASE N'EST INVENTÉE. Une valeur absente s'imprime « — » ; les lignes « non renseigné » des
/// nomenclatures s'impriment telles quelles, en italique, pour que le lecteur distingue un
/// établissement réellement peu qualifié d'un établissement dont la saisie est incomplète.
/// </summary>
public class StateducReportDocument(
    StateducReportDto report,
    byte[]? directorSignature = null,
    byte[]? officialStamp = null) : IDocument
{
    private const string NoValue = "—";

    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Rapport STATEDUC — {report.SchoolName} — {report.SchoolYearLabel}",
        Author = report.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(1.2f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(8).FontColor(Colors.Black));

            page.Header().Element(ComposeHeader);
            page.Content().PaddingTop(8).Element(ComposeBody);
            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(5).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(9);
                    left.Item().Text("Ministère de l'Éducation nationale").FontSize(8);

                    // IA / IEF : imprimées seulement si renseignées — jamais une ligne vide légendée,
                    // convention commune à tous les documents officiels du projet.
                    var administration = string.Join(
                        "  ·  ",
                        new[] { report.InspectionAcademie, report.InspectionEducationFormation }
                            .Where(v => !string.IsNullOrWhiteSpace(v)));

                    if (administration.Length > 0)
                    {
                        left.Item().PaddingTop(1).Text(administration).FontSize(8).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.RelativeItem().AlignCenter().Column(center =>
                {
                    center.Item().AlignCenter().Text("RAPPORT ANNUEL STATEDUC").Bold().FontSize(13);
                    center.Item().AlignCenter().Text($"Année scolaire {report.SchoolYearLabel}").FontSize(9);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text(report.SchoolName.ToUpperInvariant()).Bold().FontSize(10);
                    right.Item().AlignRight().Element(c => LabelledLine(c, "Code établissement", report.NationalSchoolCode));
                    right.Item().AlignRight().Element(c => LabelledLine(c, "Autorisation", report.MinistryAuthorizationNumber));
                    right.Item().AlignRight().Element(c => LabelledLine(c, "Circonscription", report.SchoolDistrictCode));
                });
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => LabelledLine(c, "Adresse", report.Address));
                row.RelativeItem().Element(c => LabelledLine(c, "Téléphone", report.Phone));
                row.RelativeItem().Element(c => LabelledLine(c, "Courriel", report.Email));
                row.RelativeItem().Element(c => LabelledLine(c, "Coordonnées GPS", report.GpsCoordinates));
            });
        });

    private void ComposeBody(IContainer container) =>
        container.Column(column =>
        {
            column.Spacing(10);

            column.Item().Element(ComposeSummary);
            column.Item().Element(ComposeEnrollmentsTable);
            column.Item().Element(ComposeAgePyramid);
            column.Item().Element(ComposeTeacherQualifications);
            column.Item().Element(ComposeTeacherStatuses);
            column.Item().Element(ComposeDataQualityNotice);
        });

    /// <summary>
    /// Bandeau de synthèse : les six chiffres que l'IEF lit en premier. Ils sont TOUS recalculés depuis
    /// les tableaux détaillés (propriétés dérivées de <see cref="StateducReportDto"/>), jamais saisis
    /// en parallèle — un bandeau qui contredirait ses propres tableaux ferait rejeter le formulaire.
    /// </summary>
    private void ComposeSummary(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("1. Synthèse de l'établissement");

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Effectif total", report.TotalStudents.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Filles", $"{report.TotalGirls} ({Percent(report.GirlsRatio)})"));
                row.RelativeItem().Element(c => StatBox(c, "Garçons", $"{report.TotalBoys} ({Percent(report.BoysRatio)})"));
                row.RelativeItem().Element(c => StatBox(c, "Redoublants", report.TotalRepeaters.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Enseignants", report.TotalTeachers.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Élèves / enseignant", Decimal(report.StudentsPerTeacher)));
                row.RelativeItem().Element(c => StatBox(c, "Élèves / classe", Decimal(report.StudentsPerClassroom)));
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Salles de classe", report.ClassroomCount.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Salles physiques", report.PhysicalRoomCount.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Bâtiments", report.BuildingCount.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Enseignants qualifiés", report.QualifiedTeachers.ToString()));
                row.RelativeItem().Element(c => StatBox(c, "Élèves sans IEN", report.StudentsWithoutIen.ToString()));
                row.RelativeItem(2).Element(c => StatBox(c, "Date d'observation",
                    report.ObservationDate.ToString("dd/MM/yyyy", FrenchCulture)));
            });
        });

    /// <summary>Tableau 2 — effectifs par niveau réglementaire, avec sa ligne TOTAL.</summary>
    private void ComposeEnrollmentsTable(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("2. Effectifs par niveau");

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f); // Niveau
                    columns.RelativeColumn(1.2f); // Cycle
                    columns.RelativeColumn(0.9f); // Garçons
                    columns.RelativeColumn(0.9f); // Filles
                    columns.RelativeColumn(0.9f); // Total
                    columns.RelativeColumn(1.0f); // % filles
                    columns.RelativeColumn(1.0f); // Redoublants
                    columns.RelativeColumn(1.0f); // Divisions
                    columns.RelativeColumn(1.2f); // Effectif moyen
                    columns.RelativeColumn(1.0f); // Sans IEN
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Niveau", left: true);
                    HeaderCell(header, "Cycle", left: true);
                    HeaderCell(header, "Garçons");
                    HeaderCell(header, "Filles");
                    HeaderCell(header, "Total");
                    HeaderCell(header, "% filles");
                    HeaderCell(header, "Redoublants");
                    HeaderCell(header, "Divisions");
                    HeaderCell(header, "Eff. / division");
                    HeaderCell(header, "Sans IEN");
                });

                foreach (var row in report.EnrollmentsByLevel)
                {
                    table.Cell().Element(BodyCell).Text(row.Level);
                    table.Cell().Element(BodyCell).Text(row.Cycle);
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Boys.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Girls.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Total.ToString()).Bold();
                    table.Cell().Element(BodyCell).AlignCenter().Text(Percent(row.GirlsRatio));
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Repeaters.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.ClassroomCount.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(Decimal(row.AverageClassSize));
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.WithoutIen.ToString());
                }

                table.Cell().ColumnSpan(2).Element(TotalCell).Text("TOTAL").Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TotalBoys.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TotalGirls.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TotalStudents.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(Percent(report.GirlsRatio)).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TotalRepeaters.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.ClassroomCount.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(Decimal(report.StudentsPerClassroom)).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.StudentsWithoutIen.ToString()).Bold();
            });
        });

    /// <summary>
    /// Tableau 3 — pyramide des âges. Une barre proportionnelle accompagne chaque tranche : le
    /// formulaire officiel est une grille de chiffres, mais le directeur qui le signe doit voir d'un
    /// coup d'œil si sa pyramide est déformée par des élèves en retard scolaire.
    /// </summary>
    private void ComposeAgePyramid(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("3. Pyramide des âges");

            var maxTotal = report.AgePyramid.Count > 0 ? report.AgePyramid.Max(r => r.Total) : 0;

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(1.4f); // Âge
                    columns.RelativeColumn(0.9f); // Garçons
                    columns.RelativeColumn(0.9f); // Filles
                    columns.RelativeColumn(0.9f); // Total
                    columns.RelativeColumn(1.0f); // % effectif
                    columns.RelativeColumn(4.5f); // Barre
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Âge révolu", left: true);
                    HeaderCell(header, "Garçons");
                    HeaderCell(header, "Filles");
                    HeaderCell(header, "Total");
                    HeaderCell(header, "% effectif");
                    HeaderCell(header, "Répartition", left: true);
                });

                foreach (var row in report.AgePyramid)
                {
                    // La tranche « âge non déterminé » est mise en italique : c'est une ANOMALIE de
                    // données, pas une classe d'âge. La confondre visuellement avec les autres la
                    // ferait passer pour un effectif normal.
                    var isAnomaly = row.Age is null;

                    var label = table.Cell().Element(BodyCell).Text(row.Label);
                    if (isAnomaly)
                    {
                        label.Italic();
                    }

                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Boys.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Girls.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Total.ToString()).Bold();
                    table.Cell().Element(BodyCell).AlignCenter()
                        .Text(Percent(StateducReportDto.Ratio(row.Total, report.TotalStudents)));

                    table.Cell().Element(BodyCell).Element(bar => ComposeBar(bar, row.Total, maxTotal));
                }
            });
        });

    /// <summary>
    /// Barre proportionnelle au plus grand effectif de la pyramide. Largeur relative, jamais une
    /// largeur absolue en points : le tableau doit rester juste si la page change de format.
    /// </summary>
    private static void ComposeBar(IContainer container, int value, int max)
    {
        if (max <= 0 || value <= 0)
        {
            container.Text("");
            return;
        }

        container.AlignMiddle().Row(row =>
        {
            row.RelativeItem(value).Height(5).Background(Colors.Grey.Darken1);

            // Le reste de la ligne est occupé par un élément vide de largeur complémentaire : c'est
            // ce complément qui rend la barre PROPORTIONNELLE plutôt que toujours pleine largeur.
            var remainder = max - value;
            if (remainder > 0)
            {
                row.RelativeItem(remainder).Height(5);
            }
        });
    }

    private void ComposeTeacherQualifications(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle)
                .Text("4. Personnel enseignant — qualifications (diplôme académique × diplôme professionnel)");

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(2.0f); // Diplôme académique
                    columns.RelativeColumn(2.0f); // Diplôme professionnel
                    columns.RelativeColumn(1.0f); // Hommes
                    columns.RelativeColumn(1.0f); // Femmes
                    columns.RelativeColumn(1.4f); // Genre non renseigné
                    columns.RelativeColumn(1.0f); // Total
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Diplôme académique", left: true);
                    HeaderCell(header, "Diplôme professionnel", left: true);
                    HeaderCell(header, "Hommes");
                    HeaderCell(header, "Femmes");
                    HeaderCell(header, "Genre non saisi");
                    HeaderCell(header, "Total");
                });

                foreach (var row in report.TeacherQualifications)
                {
                    QualificationCell(table, Describe(row.AcademicQualification),
                        row.AcademicQualification == AcademicQualification.NonRenseigne);
                    QualificationCell(table, Describe(row.ProfessionalQualification),
                        row.ProfessionalQualification == ProfessionalQualification.NonRenseigne);

                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Men.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Women.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.GenderNotReported.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Count.ToString()).Bold();
                }

                table.Cell().ColumnSpan(2).Element(TotalCell).Text("TOTAL").Bold();
                table.Cell().Element(TotalCell).AlignCenter()
                    .Text(report.TeacherQualifications.Sum(r => r.Men).ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter()
                    .Text(report.TeacherQualifications.Sum(r => r.Women).ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TeachersWithoutGender.ToString()).Bold();
                table.Cell().Element(TotalCell).AlignCenter().Text(report.TotalTeachers.ToString()).Bold();
            });

            column.Item().PaddingTop(3).Row(row =>
            {
                row.RelativeItem().Element(c => StatBox(c, "Enseignants qualifiés (diplôme professionnel)",
                    $"{report.QualifiedTeachers} / {report.TotalTeachers}"));
                row.RelativeItem().Element(c => StatBox(c, "Taux de qualification",
                    Percent(StateducReportDto.Ratio(report.QualifiedTeachers, report.TotalTeachers))));
                row.RelativeItem().Element(c => StatBox(c, "Qualification non saisie",
                    report.UnreportedQualificationTeachers.ToString()));
            });
        });

    private void ComposeTeacherStatuses(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Element(SectionTitle).Text("5. Personnel enseignant — statut administratif");

            column.Item().PaddingTop(3).Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn(3.0f);
                    columns.RelativeColumn(1.0f);
                    columns.RelativeColumn(1.0f);
                    columns.RelativeColumn(1.4f);
                    columns.RelativeColumn(1.0f);
                });

                table.Header(header =>
                {
                    HeaderCell(header, "Statut", left: true);
                    HeaderCell(header, "Hommes");
                    HeaderCell(header, "Femmes");
                    HeaderCell(header, "Genre non saisi");
                    HeaderCell(header, "Total");
                });

                foreach (var row in report.TeacherStatuses)
                {
                    QualificationCell(table, Describe(row.Status),
                        row.Status == TeacherCivilServiceStatus.NonRenseigne);

                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Men.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Women.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.GenderNotReported.ToString());
                    table.Cell().Element(BodyCell).AlignCenter().Text(row.Count.ToString()).Bold();
                }
            });
        });

    /// <summary>
    /// Avertissement de QUALITÉ DES DONNÉES, imprimé uniquement s'il y a matière. C'est la contrepartie
    /// du principe « aucune case n'est devinée » : puisque les lacunes sont comptées à part, le
    /// document doit les nommer — sans quoi le directeur signerait un formulaire dont il ne verrait
    /// pas qu'il est incomplet. Rien ne s'imprime quand la saisie est complète.
    /// </summary>
    private void ComposeDataQualityNotice(IContainer container)
    {
        var gaps = new List<string>();

        if (report.StudentsWithoutIen > 0)
        {
            gaps.Add($"{report.StudentsWithoutIen} élève(s) sans IEN renseigné");
        }

        if (report.UnreportedQualificationTeachers > 0)
        {
            gaps.Add($"{report.UnreportedQualificationTeachers} enseignant(s) sans diplôme professionnel saisi");
        }

        if (report.TeachersWithoutGender > 0)
        {
            gaps.Add($"{report.TeachersWithoutGender} enseignant(s) sans genre saisi");
        }

        var undeterminedAges = report.AgePyramid.FirstOrDefault(r => r.Age is null)?.Total ?? 0;
        if (undeterminedAges > 0)
        {
            gaps.Add($"{undeterminedAges} élève(s) à la date de naissance invraisemblable");
        }

        // Saisie complète : rien à signaler. Le conteneur est tout de même composé (vide) plutôt que
        // laissé intact — QuestPDF attend qu'un Element() reçu soit consommé, et un `return` nu
        // laisserait un conteneur jamais renseigné dans l'arbre de rendu.
        if (gaps.Count == 0)
        {
            container.Text("");
            return;
        }

        container.Border(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten4)
            .Padding(5).Column(column =>
            {
                column.Item().Text("Données incomplètes à la date d'observation").Bold().FontSize(8.5f);
                column.Item().PaddingTop(2).Text(string.Join("  ·  ", gaps)).FontSize(8);
                column.Item().PaddingTop(2)
                    .Text("Ces effectifs sont comptés à part et ne sont imputés à aucune catégorie déclarée. "
                          + "Complétez la saisie avant transmission pour que le rapport reflète l'établissement.")
                    .Italic().FontSize(7.5f).FontColor(Colors.Grey.Darken2);
            });
    }

    private void ComposeFooter(IContainer container) =>
        container.BorderTop(0.75f).BorderColor(Colors.Grey.Medium).PaddingTop(4).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text(text =>
                {
                    text.Span("Généré le ").FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.Span(report.GeneratedAt.ToString("dd/MM/yyyy à HH:mm", FrenchCulture))
                        .FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.Span("  ·  Effectifs arrêtés au ").FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.Span(report.ObservationDate.ToString("dd/MM/yyyy", FrenchCulture))
                        .FontSize(7).FontColor(Colors.Grey.Darken2);
                });

                left.Item().Text(text =>
                {
                    text.Span("Page ").FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.CurrentPageNumber().FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.Span(" / ").FontSize(7).FontColor(Colors.Grey.Darken2);
                    text.TotalPages().FontSize(7).FontColor(Colors.Grey.Darken2);
                });
            });

            row.RelativeItem().AlignRight().Column(right =>
            {
                // Signature/cachet réels si le Directeur les a téléversés ; sinon rien — jamais une
                // image inventée, même convention que le bulletin.
                if (directorSignature is not null)
                {
                    right.Item().AlignRight().Height(22).Image(directorSignature).FitArea();
                }

                right.Item().AlignRight().Text("Le Chef d'Établissement").Bold().FontSize(8);

                if (officialStamp is not null)
                {
                    right.Item().PaddingTop(2).AlignRight().Height(38).Width(38)
                        .Image(officialStamp).FitArea();
                }
            });
        });

    // ---------------------------------------------------------------- Fragments de mise en page

    /// <summary>
    /// Cellule de libellé de nomenclature. Une valeur « non renseigné » s'imprime en ITALIQUE : c'est
    /// ce qui la distingue d'une catégorie réelle sur un document qu'on lit en diagonale.
    /// </summary>
    private static void QualificationCell(TableDescriptor table, string label, bool isUnreported)
    {
        var cell = table.Cell().Element(BodyCell).Text(label);
        if (isUnreported)
        {
            cell.Italic().FontColor(Colors.Grey.Darken2);
        }
    }

    private static void HeaderCell(TableCellDescriptor header, string label, bool left = false)
    {
        var cell = header.Cell()
            .Border(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3)
            .PaddingVertical(3).PaddingHorizontal(3);

        (left ? cell : cell.AlignCenter()).Text(label).Bold().FontSize(7.5f);
    }

    private static IContainer BodyCell(IContainer c) =>
        c.Border(0.5f).BorderColor(Colors.Grey.Medium).PaddingVertical(2.5f).PaddingHorizontal(3);

    private static IContainer TotalCell(IContainer c) =>
        c.Border(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3)
            .PaddingVertical(3).PaddingHorizontal(3);

    private static IContainer SectionTitle(IContainer c) =>
        c.BorderBottom(0.75f).BorderColor(Colors.Black).PaddingBottom(2).DefaultTextStyle(
            t => t.Bold().FontSize(9.5f));

    private static void StatBox(IContainer container, string label, string value) =>
        container.PaddingRight(4).Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(4).Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
            column.Item().PaddingTop(1).Text(value).Bold().FontSize(10);
        });

    private static void LabelledLine(IContainer container, string label, string? value) =>
        container.Text(text =>
        {
            text.Span($"{label} : ").SemiBold().FontSize(7.5f);
            text.Span(value ?? NoValue).FontSize(7.5f);
        });

    // ------------------------------------------------------------------------------ Formatage

    /// <summary>Pourcentage à une décimale, ou « — » quand le ratio est indéfini (dénominateur nul).</summary>
    private static string Percent(decimal? ratio) =>
        ratio is { } value ? value.ToString("0.#", FrenchCulture) + " %" : NoValue;

    private static string Decimal(decimal? value) =>
        value is { } v ? v.ToString("0.#", FrenchCulture) : NoValue;

    private static string Describe(AcademicQualification value) => value switch
    {
        AcademicQualification.NonRenseigne => "Non renseigné",
        AcademicQualification.Aucun => "Aucun",
        AcademicQualification.BFEM => "BFEM",
        AcademicQualification.BAC => "Baccalauréat",
        AcademicQualification.Licence => "Licence",
        AcademicQualification.Master => "Master",
        _ => "Doctorat"
    };

    private static string Describe(ProfessionalQualification value) => value switch
    {
        ProfessionalQualification.NonRenseigne => "Non renseigné",
        ProfessionalQualification.Aucun => "Aucun",
        ProfessionalQualification.CEAP => "CEAP",
        ProfessionalQualification.CAP => "CAP",
        ProfessionalQualification.CAEM => "CAEM",
        _ => "CAES"
    };

    private static string Describe(TeacherCivilServiceStatus value) => value switch
    {
        TeacherCivilServiceStatus.NonRenseigne => "Non renseigné",
        TeacherCivilServiceStatus.Fonctionnaire => "Fonctionnaire",
        TeacherCivilServiceStatus.Contractuel => "Contractuel",
        TeacherCivilServiceStatus.Vacataire => "Vacataire",
        TeacherCivilServiceStatus.Volontaire => "Volontaire",
        _ => "Bénévole"
    };
}
