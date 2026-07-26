using System.Globalization;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin de notes en PDF, format A5 portrait (ticket JGK-G03). Reproduit
/// docs/design-references/bulletin-reference.png (AGENTS.md règle #12) : en-tête administratif
/// IA/IEF/&lt;cycle&gt;, titre entre doubles filets, bloc d'identité encadré, tableau des disciplines avec
/// appréciations, lignes TOTAL/Moyenne avec assiduité, rangée des distinctions, puis Décision du
/// Conseil + Observations (gauche) et récapitulatif des moyennes + signature du Chef d'établissement
/// avec emplacement de cachet (droite).
///
/// Une case de la référence reste volontairement VIDE — visuellement présente, jamais remplie d'une
/// donnée inventée — car rien dans le système ne l'alimente : T.H. La case « Classe redoublée » est,
/// elle, cochée [X]/[ ] d'après <see cref="ReportCardDto.IsRepeating"/> (feature F, Enrollment.IsRepeating).
/// L'assiduité (Absences/Retards) s'imprime « - » tant qu'aucun appel n'a été fait sur la période (voir
/// ReportCardDto), jamais un zéro trompeur. La distinction du conseil (Blâme… Félicitations), la
/// Décision du Conseil (Admis/Redouble/Exclusion) et les Observations, elles, SONT modélisées
/// (ReportCardRemark) — cochées/remplies si saisies via l'écran dédié, vides sinon.
///
/// Le logo n'apparaît PAS : l'en-tête de la référence est purement administratif (IA/IEF/établissement),
/// sans aucun logo. Le paramètre est conservé pour ne pas casser le contrat
/// IReportCardPdfGenerator — il est simplement ignoré à la mise en page.
/// </summary>
public class ReportCardDocument(ReportCardDto reportCard, byte[]? logo) : IDocument
{
    /// <summary>Filet noir standard de la référence (tableaux, encadrés).</summary>
    private const float RuleThickness = 0.75f;

    /// <summary>
    /// Cycle à notation simplifiée (Maternelle, Primaire), lu sur <see cref="ReportCardDto.Cycle"/> — la
    /// classe de l'élève, résolue dans ReportCardDataService. Ces cycles n'ont ni coefficients, ni
    /// mentions/distinctions, ni appréciations (système réservé au secondaire, étape 3) : le tableau et
    /// la mise en page s'adaptent en conséquence, tandis que le rendu /20 reste strictement inchangé.
    /// Le prédicat vit sur CycleTypeExtensions, partagé avec GradingScaleGuard et GetGradeSummary.
    /// </summary>
    private bool IsPrimaire => reportCard.Cycle.UsesSimplifiedGrading();

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Bulletin de notes — {reportCard.StudentFullName}",
        Author = reportCard.SchoolName
    };

    public void Compose(IDocumentContainer container) => ComposePage(container);

    /// <summary>
    /// Compose UNE page A5 dans <paramref name="container"/> — extrait de <see cref="Compose"/> pour être
    /// appelé plusieurs fois sur le MÊME conteneur (une page par élève) par <see cref="ClassBulletinsDocument"/>,
    /// qui fusionne les bulletins de toute une classe en un seul PDF. Même mise en page que le bulletin
    /// individuel, aucune logique dupliquée.
    /// </summary>
    internal void ComposePage(IDocumentContainer container)
    {
        // Le logo est volontairement ignoré (voir remarque de classe) : la référence n'en montre pas.
        _ = logo;

        container.Page(page =>
        {
            page.Size(PageSizes.A5);

            // Marges serrées (5-8 mm) : tout le gabarit tient sur UNE page A5, sans seconde page blanche.
            page.Margin(7, Unit.Millimetre);

            // Times New Roman : absente des dépôts Linux (police propriétaire) — le Dockerfile de
            // production mappe ce nom vers Liberation Serif, un clone à métriques identiques, via un
            // alias fontconfig. Rien à faire ici pour que les deux environnements donnent le même rendu.
            page.DefaultTextStyle(text => text.FontFamily("Times New Roman").FontSize(7.5f).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Spacing(3);
                column.Item().Element(ComposeHeader);
                column.Item().Element(ComposeTitle);
                column.Item().Element(ComposeIdentity);
                // Primaire (/10) : tableau épuré sans coefficients/appréciations et SANS rangée de
                // distinctions du conseil. Secondaire (/20) : rendu d'origine, strictement inchangé.
                column.Item().PaddingTop(2).Element(IsPrimaire ? ComposeGradesTablePrimaire : ComposeGradesTable);
                if (!IsPrimaire)
                {
                    column.Item().Element(ComposeDisciplinaryMentionsRow);
                }
                column.Item().PaddingTop(5).Element(ComposeDecisionAndRecap);
                column.Item().PaddingTop(4).Element(ComposeFooter);
            });
        });
    }

    /// <summary>
    /// En-tête administratif de la référence : IA / IEF / établissement à gauche (en majuscules), année
    /// scolaire et période à droite, alignées sur les deux premières lignes. Une valeur non renseignée
    /// laisse sa ligne vide après le libellé — jamais une valeur inventée.
    ///
    /// Le LIBELLÉ de chaque ligne est en gras (IA, IEF, ÉCOLE ÉLÉMENTAIRE DE / COLLÈGE DE / LYCÉE DE),
    /// la valeur renseignée par l'école reste en normal : c'est le contraste entre les deux qui fait
    /// ressortir la structure administrative, là où tout mettre en gras la ferait disparaître.
    /// </summary>
    private void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(3).Column(left =>
            {
                left.Item().Element(c => HeaderLine(c, "IA", Upper(reportCard.InspectionAcademie)));
                left.Item().Element(c => HeaderLine(c, "IEF", Upper(reportCard.InspectionEducationFormation)));

                // Préfixe résolu par cycle en amont (SchoolHeading) : « ÉCOLE ÉLÉMENTAIRE DE » pour un
                // CM2, « COLLÈGE DE » pour une 5e, « LYCÉE DE » pour une Terminale — le même
                // établissement édite les trois. AUCUN repli sur le nom légal de l'école (Identité de
                // l'établissement, utilisé sur le reçu) : ce champ n'a pas sa place sur le bulletin,
                // même quand HeadingName est vide — la ligne s'imprime alors réduite à son préfixe,
                // exactement comme IA et IEF ci-dessus s'impriment réduites au leur.
                left.Item().Element(c => HeaderLine(c, reportCard.HeadingPrefix, Upper(reportCard.HeadingName)));
            });

            row.RelativeItem(2).Column(right =>
            {
                right.Item().AlignRight().Text(t =>
                {
                    t.Span("Année Scolaire : ").Bold().FontSize(8.5f);
                    t.Span(reportCard.SchoolYearLabel).FontSize(8.5f);
                });
                right.Item().AlignRight().Text(reportCard.TermLabel).Bold().FontSize(8.5f);
            });
        });

        // Libellé en gras, valeur en normal — une seule définition pour les trois lignes de gauche,
        // sans quoi la mise en forme dériverait de l'une à l'autre.
        static void HeaderLine(IContainer container, string label, string value) =>
            container.Text(text =>
            {
                text.Span($"{label} : ").Bold().FontSize(8.5f);
                text.Span(value).FontSize(8.5f);
            });
    }

    /// <summary>Titre centré entre DEUX doubles filets horizontaux, comme sur la référence.</summary>
    private static void ComposeTitle(IContainer container)
    {
        container.Column(column =>
        {
            column.Item().Element(DoubleRule);
            column.Item().PaddingVertical(2).AlignCenter().Text("BULLETIN DE NOTES").Bold().FontSize(12);
            column.Item().Element(DoubleRule);
        });

        static void DoubleRule(IContainer c) => c.Column(rule =>
        {
            rule.Item().LineHorizontal(RuleThickness).LineColor(Colors.Black);
            rule.Item().Height(1.2f);
            rule.Item().LineHorizontal(RuleThickness).LineColor(Colors.Black);
        });
    }

    /// <summary>
    /// Bloc d'identité encadré, trois lignes fixes : Prénoms/Nom (gras, corps plus grand), naissance et
    /// classe, matricule et effectif. « Classe Redoublée » est cochée [X]/[ ] selon IsRepeating (feature F).
    /// </summary>
    private void ComposeIdentity(IContainer container)
    {
        var (prenoms, nom) = SplitFullName(reportCard.StudentFullName);

        container.Border(RuleThickness).BorderColor(Colors.Black).Padding(4).Table(table =>
        {
            // LES TROIS LIGNES PARTAGENT CES MÊMES COLONNES : c'est ce partage — pas un ajustement de
            // largeurs au jugé — qui aligne "Nom" avec "Classe" et "Classe Redoublée" sur la même
            // verticale. "Prénoms" fusionne les deux premières colonnes (ColumnSpan) pour lui laisser
            // sa place habituelle, plus large.
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2f);
                columns.RelativeColumn(1.6f);
                columns.RelativeColumn(1.4f);
            });

            table.Cell().ColumnSpan(2).Element(Cell).Text(t =>
            {
                t.Span("Prénoms : ").FontSize(8.5f);
                t.Span(prenoms).Bold().FontSize(9.5f);
            });
            table.Cell().Element(Cell).Text(t =>
            {
                t.Span("Nom : ").FontSize(8.5f);
                t.Span(nom).Bold().FontSize(9.5f);
            });

            table.Cell().Element(Cell).Text($"Né(e) le : {FormatDate(reportCard.BirthDate)}");
            table.Cell().Element(Cell).Text($"à : {reportCard.BirthPlace}");
            table.Cell().Element(Cell).Text(t =>
            {
                t.Span("Classe : ");
                t.Span(reportCard.ClassroomName).Bold();
            });

            table.Cell().Element(Cell).Text($"Matricule : {reportCard.Matricule}");
            table.Cell().Element(Cell).Text($"Nbre d'élèves : {reportCard.ClassSize}");
            // Classe redoublée (feature F) : cochée [X] si l'inscription porte IsRepeating, [ ] sinon —
            // même convention de coche que la rangée des distinctions du conseil.
            table.Cell().Element(Cell).Text($"Classe Redoublée : [{(reportCard.IsRepeating ? "X" : " ")}]");
        });

        static IContainer Cell(IContainer c) => c.PaddingVertical(1.5f);
    }

    private void ComposeGradesTable(IContainer container)
    {
        // Barème d'espacement des lignes : moins il y a de matières, plus chaque ligne s'étire, pour
        // qu'une classe à 2-3 matières ne laisse pas un grand vide sous un tableau minuscule. Repère :
        // 12 matières (le cas testé par JGK-G03, "tient sur une page") retombe exactement sur les 2 pt
        // fixes d'avant ce changement — le plafond bas est donc déjà éprouvé pour ne jamais déborder.
        var rowPadding = Math.Clamp(24f / Math.Max(reportCard.Subjects.Count, 1), 2f, 8f);

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.9f);  // Disciplines
                columns.RelativeColumn(0.9f);  // Devoir
                columns.RelativeColumn(0.9f);  // Composition
                columns.RelativeColumn(1.05f); // Moyenne
                columns.RelativeColumn(0.7f);  // Coefficient
                columns.RelativeColumn(1.05f); // Moyenne x Coef
                columns.RelativeColumn(0.6f);  // T.H
                columns.RelativeColumn(0.9f);  // Rang
                columns.RelativeColumn(1.85f); // Appréciations
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("DISCIPLINES").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Devoir").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Comp").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text($"Moy/{reportCard.GradingScale}").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Coef").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Moy x").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("T.H").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).Text("Appréciations").Bold().FontSize(7);
            });

            foreach (var subject in reportCard.Subjects)
            {
                table.Cell().Element(BodyCell).Text(subject.SubjectName);
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.DevoirAverage));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.Composition));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.Average));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.Coefficient));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.WeightedPoints));
                table.Cell().Element(BodyCell).Text(""); // T.H : signification non établie, case vide.
                table.Cell().Element(BodyCell).AlignCenter().Text(reportCard.SubjectRanks.GetValueOrDefault(subject.SubjectId, 0) is > 0 and var rank ? rank.ToString() : "—");
                table.Cell().Element(BodyCell).Text(reportCard.SubjectAppreciations.GetValueOrDefault(subject.SubjectId) ?? "");
            }

            // Ligne TOTAL de la référence : totaux Coef et Moy x, puis la case « Absences » à droite.
            table.Cell().Element(TotalCell).Text("TOTAL").Bold();
            table.Cell().ColumnSpan(3).Element(TotalCell).Text("");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatGrade(reportCard.TotalCoefficients)).Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatGrade(reportCard.TotalPoints)).Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).Text("Absences");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Absences));

            // Ligne Moyenne de la référence : moyenne générale, rang, retards, absences totales — dans
            // la MÊME table pour que les filets verticaux restent alignés avec le tableau des notes.
            table.Cell().Element(TotalCell).Text($"Moyenne : {FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale}").Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).AlignCenter().Text("Rang");
            table.Cell().Element(TotalCell).AlignCenter().Text(reportCard.GeneralRank.ToString()).Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).AlignCenter().Text("Retards");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Retards));

            // Corps réduit : « Abs. Tot » doit tenir sur UNE ligne dans sa colonne étroite, sans faire
            // gonfler la hauteur de la rangée Moyenne.
            table.Cell().Element(TotalCell).AlignCenter().Text("Abs. Tot").FontSize(6.5f);
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.TotalAbsences));
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(3);
        IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(rowPadding).PaddingHorizontal(3);
        static IContainer TotalCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(3);
    }

    /// <summary>
    /// Variante PRIMAIRE du tableau des notes (/10, étape 3). Le cycle primaire n'a NI système de
    /// coefficients, NI mentions/distinctions, NI appréciations (réservés au secondaire) : on retire donc
    /// les colonnes Coefficient, « Moy x » et Appréciations, ainsi que les totaux de coefficients de la
    /// rangée TOTAL. Restent les disciplines, Devoir/Composition, la moyenne /10, T.H (case vide, comme
    /// au secondaire), le rang et l'assiduité. Le rendu Collège/Lycée passe, lui, par
    /// <see cref="ComposeGradesTable"/>, laissé strictement inchangé pour éviter toute régression visuelle.
    /// </summary>
    private void ComposeGradesTablePrimaire(IContainer container)
    {
        // Même barème d'espacement des lignes que le secondaire (voir ComposeGradesTable).
        var rowPadding = Math.Clamp(24f / Math.Max(reportCard.Subjects.Count, 1), 2f, 8f);

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(3.4f);  // Disciplines
                columns.RelativeColumn(1.0f);  // Devoir
                columns.RelativeColumn(1.0f);  // Composition
                columns.RelativeColumn(1.15f); // Moyenne /10
                columns.RelativeColumn(0.7f);  // T.H
                columns.RelativeColumn(1.0f);  // Rang
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("DISCIPLINES").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Devoir").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Comp").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text($"Moy/{reportCard.GradingScale}").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("T.H").Bold().FontSize(7);
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold().FontSize(7);
            });

            foreach (var subject in reportCard.Subjects)
            {
                table.Cell().Element(BodyCell).Text(subject.SubjectName);
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.DevoirAverage));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(subject.Composition));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(subject.Average));
                table.Cell().Element(BodyCell).Text(""); // T.H : signification non établie, case vide.
                table.Cell().Element(BodyCell).AlignCenter().Text(reportCard.SubjectRanks.GetValueOrDefault(subject.SubjectId, 0) is > 0 and var rank ? rank.ToString() : "—");
            }

            // Rangée moyenne générale + rang : pas de totaux de coefficients (le primaire n'en a pas).
            table.Cell().ColumnSpan(4).Element(TotalCell).Text($"Moyenne : {FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale}").Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text("Rang").Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text(reportCard.GeneralRank.ToString()).Bold();

            // Assiduité du trimestre — sans lien avec coefficients/mentions, conservée comme au secondaire.
            table.Cell().Element(TotalCell).AlignCenter().Text("Absences");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Absences));
            table.Cell().Element(TotalCell).AlignCenter().Text("Retards");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Retards));
            table.Cell().Element(TotalCell).AlignCenter().Text("Abs. Tot").FontSize(6.5f);
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.TotalAbsences));
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(3);
        IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(rowPadding).PaddingHorizontal(3);
        static IContainer TotalCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(3);
    }

    /// <summary>
    /// Ligne des distinctions du conseil (Blâme… Félicitations) — chaque case porte une coche « [ ] »/
    /// « [X] » : COCHÉE, grisée et en gras pour <see cref="ReportCardDto.DisciplinaryMention"/>, vide
    /// sinon. Saisie via l'écran « Observations du conseil » (PUT /report-cards/remark), jamais
    /// attribuée automatiquement.
    /// </summary>
    private void ComposeDisciplinaryMentionsRow(IContainer container)
    {
        (DisciplinaryMention Value, string Label)[] mentions =
        [
            (DisciplinaryMention.Blame, "Blâme"),
            (DisciplinaryMention.Avertissement, "Avertissement"),
            (DisciplinaryMention.TableauHonneur, "Tableau d'honneur"),
            (DisciplinaryMention.Encouragements, "Encouragements"),
            (DisciplinaryMention.Felicitations, "Félicitations")
        ];

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                foreach (var _ in mentions) columns.RelativeColumn();
            });

            foreach (var (value, label) in mentions)
            {
                var isChecked = value == reportCard.DisciplinaryMention;

                var cell = table.Cell().Border(0.5f).BorderColor(Colors.Black);
                if (isChecked)
                {
                    cell = cell.Background(Colors.Grey.Lighten3);
                }

                var text = cell.PaddingVertical(2).AlignCenter().Text($"[{(isChecked ? "X" : " ")}] {label}").FontSize(6.5f);
                if (isChecked)
                {
                    text.Bold();
                }
            }
        });
    }

    private void ComposeDecisionAndRecap(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem(1.1f).Element(ComposeDecisionDuConseil);
            row.ConstantItem(10);
            row.RelativeItem(1).Element(ComposeAnnualRecap);
        });
    }

    /// <summary>
    /// Bloc « Décision du Conseil » : trois lignes fixes, chacune avec une case à droite. La case de la
    /// ligne correspondant à <see cref="ReportCardDto.CouncilDecision"/> porte un « X » gras ; les deux
    /// autres restent vides — jamais plus d'une coche, jamais une décision par défaut inventée si rien
    /// n'a été saisi (voir ReportCardRemark.CouncilDecision, nullable).
    /// </summary>
    private void ComposeDecisionDuConseil(IContainer container)
    {
        (CouncilDecision Value, string Label)[] decisions =
        [
            (CouncilDecision.Admitted, "Admis(e) en classe supérieure"),
            (CouncilDecision.AllowedToRepeat, "Autorisé(e) à redoubler"),
            (CouncilDecision.Excluded, "Exclusion")
        ];

        container.Border(RuleThickness).BorderColor(Colors.Black).Column(column =>
        {
            column.Item().Background(Colors.Grey.Lighten3).PaddingVertical(2).AlignCenter().Text("Décision du Conseil").Bold();

            foreach (var (value, label) in decisions)
            {
                var isChecked = value == reportCard.CouncilDecision;

                column.Item().BorderTop(0.5f).BorderColor(Colors.Black).Row(row =>
                {
                    row.RelativeItem().PaddingVertical(2.5f).PaddingHorizontal(3).Text(label);

                    // Petit carré à droite — grisé et coché [X] en gras pour la ligne retenue, vide sinon.
                    var box = row.ConstantItem(18).BorderLeft(0.5f).BorderColor(Colors.Black).PaddingVertical(2.5f);
                    if (isChecked)
                    {
                        box = box.Background(Colors.Grey.Lighten3);
                    }

                    var mark = box.AlignCenter().Text(isChecked ? "X" : "");
                    if (isChecked)
                    {
                        mark.Bold();
                    }
                });
            }
        });
    }

    private void ComposeAnnualRecap(IContainer container)
    {
        container.Border(RuleThickness).BorderColor(Colors.Black).Column(column =>
        {
            foreach (var recap in reportCard.TermRecaps)
            {
                column.Item().BorderBottom(0.5f).BorderColor(Colors.Black).Row(row =>
                {
                    row.RelativeItem().PaddingVertical(2.5f).PaddingHorizontal(3).Text($"Moy. {recap.TermLabel}");
                    row.ConstantItem(36).PaddingVertical(2.5f).AlignRight().PaddingRight(3)
                        .Text(recap.Average is { } avg ? FormatGrade(avg) : "—");
                });
            }

            column.Item().BorderBottom(0.5f).BorderColor(Colors.Black).Row(row =>
            {
                row.RelativeItem().PaddingVertical(2.5f).PaddingHorizontal(3).Text("Moyenne annuelle").Bold();
                row.ConstantItem(36).PaddingVertical(2.5f).AlignRight().PaddingRight(3)
                    .Text(reportCard.AnnualAverage is { } annual ? FormatGrade(annual) : "—").Bold();
            });

            column.Item().Row(row =>
            {
                row.RelativeItem().PaddingVertical(2.5f).PaddingHorizontal(3).Text("Rang");
                row.ConstantItem(36).PaddingVertical(2.5f).AlignRight().PaddingRight(3)
                    .Text(reportCard.AnnualRank is { } rank ? rank.ToString() : "—");
            });
        });
    }

    /// <summary>
    /// Pied de la référence : Observations du conseil à gauche — le cadre affiche le texte saisi via
    /// PUT /report-cards/remark, ou reste vide (mais visible, MinHeight) tant que rien n'est écrit —
    /// signature du Chef d'établissement avec l'emplacement du cachet rond à droite.
    /// </summary>
    private void ComposeFooter(IContainer container)
    {
        // Cercle en pointillés matérialisant l'emplacement du cachet officiel rond de la référence.
        const string stampCircleSvg =
            """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><circle cx="50" cy="50" r="46" fill="none" stroke="#9CA3AF" stroke-width="2" stroke-dasharray="5 4"/></svg>""";

        container.Row(row =>
        {
            row.RelativeItem(1.1f).Column(left =>
            {
                left.Item().Text("Observations du conseil des professeurs").Bold();

                // AlignMiddle sur le CONTENEUR (centrage vertical dans le cadre) + AlignCenter sur le
                // TEXTE lui-même (chaque ligne centrée horizontalement, y compris si le texte passe
                // sur plusieurs lignes) — centrer le conteneur au lieu du texte donnerait une largeur
                // "naturelle" non contrainte au bloc de texte et l'empêcherait de retourner à la ligne.
                left.Item().PaddingTop(2).Border(0.5f).BorderColor(Colors.Black).MinHeight(48)
                    .Padding(3).AlignMiddle()
                    .Text(reportCard.CouncilObservations ?? "").Bold().FontSize(9.5f).AlignCenter();
            });

            row.ConstantItem(10);

            row.RelativeItem(1).Column(right =>
            {
                right.Item().AlignCenter().Text("Le Chef d'Établissement").Bold();
                right.Item().PaddingTop(3).AlignCenter().Height(46).Width(46).Svg(stampCircleSvg);
            });
        });
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>Majuscules pour l'en-tête administratif ; chaîne vide si non renseigné (ligne imprimée vide).</summary>
    internal static string Upper(string? value) => value?.ToUpperInvariant() ?? "";

    /// <summary>« - » tant qu'aucun appel n'a été fait sur la période — jamais un zéro trompeur.</summary>
    internal static string FormatOptionalCount(int? value) => value?.ToString() ?? "-";

    /// <summary>
    /// La référence sépare Prénoms et Nom, le modèle ne porte qu'un FullName : le DERNIER mot est
    /// affiché comme nom de famille, le reste comme prénoms — l'usage sénégalais (« Mame Diarra Bousso
    /// FAYE »). Un nom en un seul mot s'affiche côté Prénoms, la case Nom reste vide. Simple heuristique
    /// d'AFFICHAGE : rien n'est modifié en base. <c>internal</c> pour ReportCardDocumentTests.
    /// </summary>
    internal static (string Prenoms, string Nom) SplitFullName(string fullName)
    {
        var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Length switch
        {
            0 => ("", ""),
            1 => (tokens[0], ""),
            _ => (string.Join(' ', tokens[..^1]), tokens[^1])
        };
    }

    /// <summary>« - » quand la note n'a pas encore été saisie (Devoir ou Composition manquant) — jamais un zéro trompeur.</summary>
    internal static string FormatOptionalGrade(decimal? value) => value is { } v ? FormatGrade(v) : "-";

    /// <summary>
    /// Volume 1 §8.5 — suppression des décimales inutiles : 17,00 → 17 ; 15,50 → 15,5. Arrondi à 2
    /// décimales avant affichage (une moyenne pondérée peut porter bien plus de décimales brutes).
    /// Séparateur décimal virgule, comme la référence visuelle ("11,13", "9,5625"). <c>internal</c> pour
    /// être exercée directement par ReportCardDocumentTests (voir InternalsVisibleTo du csproj).
    /// </summary>
    internal static string FormatGrade(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero)
            .ToString("0.##", CultureInfo.InvariantCulture)
            .Replace('.', ',');
}
