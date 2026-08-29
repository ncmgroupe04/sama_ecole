using System.Globalization;
using SamaEcole.Application.Common;
using SamaEcole.Application.ReportCards.Queries.GetReportCardPdf;
using SamaEcole.Domain.Enums;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin de notes en PDF, format A5 portrait (ticket JGK-G03). Reproduit
/// docs/design-references/bulletin-reference.png (AGENTS.md règle #12) : en-tête administratif
/// IA/IEF/&lt;cycle&gt;, titre entre doubles filets, bloc d'identité en texte brut (sans encadré, comme la
/// référence), tableau des disciplines à neuf colonnes et à largeurs fixes pour les chiffres, lignes
/// TOTAL/Moyenne avec assiduité, rangée des distinctions, puis Décision du Conseil + Observations
/// (gauche) et récapitulatif des moyennes + signature du Chef d'établissement avec emplacement de
/// cachet (droite).
///
/// Une case de la référence reste volontairement VIDE — visuellement présente, jamais remplie d'une
/// donnée inventée — car rien dans le système ne l'alimente : T.H. Le champ « Classe redoublée » porte,
/// lui, « Oui »/« Non » d'après <see cref="ReportCardDto.IsRepeating"/> (feature F, Enrollment.IsRepeating).
/// L'assiduité (Absences/Retards) s'imprime « - » tant qu'aucun appel n'a été fait sur la période (voir
/// ReportCardDto), jamais un zéro trompeur. La distinction du conseil (Blâme… Félicitations), la
/// Décision du Conseil (Admis/Redouble/Exclusion) et les Observations, elles, SONT modélisées
/// (ReportCardRemark) — cochées/remplies si saisies via l'écran dédié, vides sinon.
///
/// Le logo n'apparaît PAS : l'en-tête de la référence est purement administratif (IA/IEF/établissement),
/// sans aucun logo. Le paramètre est conservé pour ne pas casser le contrat
/// IReportCardPdfGenerator — il est simplement ignoré à la mise en page.
///
/// <paramref name="directorSignature"/>/<paramref name="officialStamp"/> (Paramètres → Établissement,
/// SchoolSettings) s'impriment dans le pied de page quand disponibles ; sinon on retombe sur les
/// emplacements vides/en pointillés d'origine — jamais une image inventée.
/// </summary>
public class ReportCardDocument(ReportCardDto reportCard, byte[]? logo, byte[]? directorSignature = null, byte[]? officialStamp = null) : IDocument
{
    /// <summary>Filet noir standard de la référence (tableaux, encadrés).</summary>
    private const float RuleThickness = 0.75f;

    /// <summary>
    /// Corps du tableau des disciplines — en-têtes compris. Un demi-point au-dessus du corps courant du
    /// document (7,5 pt) : c'est le tableau que le tuteur lit en premier, et les colonnes de chiffres,
    /// désormais à largeur fixe, ont la place de le porter sans qu'aucune valeur ne se replie.
    /// </summary>
    private const float TableFontSize = 8f;

    /// <summary>
    /// Rembourrage horizontal d'une cellule de tableau. Serré à dessein : chaque point pris ici est un
    /// point rendu au CONTENU des colonnes étroites (Coef, T.H, Rang), les premières à faire déborder.
    /// </summary>
    private const float CellPadding = 2f;

    /// <summary>
    /// Nombre de lignes que le tableau doit porter sur UNE page A5 sans jamais déborder — 18 lignes de
    /// grille réelle (le modèle du primaire : 6 domaines, de « P. Alphabétique » à « Anglais »), plus
    /// <see cref="ReserveRows"/> lignes de réserve pour l'école qui en ajoute une ou deux.
    ///
    /// Cette réserve n'est PAS imprimée : aucun des trois tableaux ne dessine de ligne vide de
    /// remplissage — ils itèrent sur les lignes réellement configurées. La réserve est un engagement de
    /// PLACE, pas un gabarit fixe : une grille à 5 lignes s'imprime en 5 lignes, étirées pour occuper la
    /// page ; une grille à 20 tient encore, resserrée. C'est <see cref="RowMetricsFor"/> qui fait varier
    /// l'interligne et le corps entre ces deux extrêmes.
    /// </summary>
    private const int NominalRowCapacity = 18;

    /// <summary>Lignes tenues en réserve au-delà de <see cref="NominalRowCapacity"/> — voir cette dernière.</summary>
    private const int ReserveRows = 2;

    /// <summary>Capacité garantie du tableau, réserve comprise : 20 lignes sur une seule page A5.</summary>
    private const int MaxRowCapacity = NominalRowCapacity + ReserveRows;

    /// <summary>
    /// En deçà de ce nombre de lignes, le tableau garde son corps plein et se contente d'étirer
    /// l'interligne : c'est le cas de l'immense majorité des bulletins (7 disciplines au secondaire,
    /// 12 au maximum du ticket JGK-G03), dont le rendu ne doit pas bouger d'un point.
    /// </summary>
    private const int ComfortRows = 12;

    /// <summary>
    /// Interligne et corps du tableau, en fonction du nombre de lignes à imprimer — SOURCE UNIQUE pour
    /// les trois variantes (secondaire, primaire, grille APC), qui portaient jusqu'ici trois barèmes
    /// divergents calibrés séparément. Trois barèmes, c'était trois capacités différentes et deux
    /// d'entre elles inconnues : celle du secondaire s'arrêtait à 20 lignes, sans marge.
    ///
    /// Deux régimes, dans cet ordre :
    ///   • jusqu'à <see cref="ComfortRows"/> lignes, seul l'INTERLIGNE varie (de 8 pt pour 3 lignes à
    ///     2 pt pour 12) — le corps reste plein, et une classe à peu de matières ne laisse pas un grand
    ///     vide sous un tableau minuscule ;
    ///   • au-delà, l'interligne est déjà au plancher : c'est le CORPS qui cède, linéairement, jusqu'à
    ///     85 % de sa taille à <see cref="MaxRowCapacity"/> lignes. Une grille de 20 lignes reste lisible
    ///     à l'impression là où elle passait sur une seconde page.
    ///
    /// Au-delà de la capacité garantie, les deux valeurs restent au plancher : le bulletin fait alors au
    /// mieux — quitte à une seconde page — plutôt que de rétrécir le texte jusqu'à l'illisible.
    /// </summary>
    private static (float FontSize, float Padding) RowMetricsFor(int lineCount)
    {
        var rows = Math.Max(lineCount, 1);

        if (rows <= ComfortRows)
        {
            return (TableFontSize, Math.Clamp(24f / rows, 2f, 8f));
        }

        // Progression de 0 (ComfortRows lignes) à 1 (capacité maximale), bornée au-delà.
        var density = Math.Min((rows - ComfortRows) / (float)(MaxRowCapacity - ComfortRows), 1f);

        return (TableFontSize - density * (TableFontSize - MinTableFontSize),
                DensePadding + (1f - density) * (2f - DensePadding));
    }

    /// <summary>Corps plancher du tableau, atteint à <see cref="MaxRowCapacity"/> lignes — 85 % du corps plein.</summary>
    private const float MinTableFontSize = TableFontSize * 0.85f;

    /// <summary>Interligne plancher, atteint à <see cref="MaxRowCapacity"/> lignes.</summary>
    private const float DensePadding = 1f;

    /// <summary>
    /// Largeurs FIXES (en points PDF) des sept colonnes de chiffres du tableau des disciplines. Le
    /// contenu de ces colonnes a un gabarit connu d'avance — une note, un coefficient, un rang — alors
    /// que « Disciplines » et « Appréciations » portent du texte de longueur imprévisible : figer les
    /// premières, c'est donner tout le reste de la page aux secondes.
    ///
    /// Repère de dimensionnement : A5 portrait à marges de 7 mm = ~380 pt utiles. Ces sept colonnes en
    /// consomment 205, les deux colonnes de texte se partagent les ~175 restants (25 / 20).
    /// Calibrées à 8 pt (<see cref="TableFontSize"/>) sur la valeur la plus large que chacune peut
    /// recevoir, en-tête inclus — ne pas réduire sans revérifier le rendu.
    /// </summary>
    private static class NumericColumnWidths
    {
        /// <summary>Devoir, Composition — « 9,56 » sous un en-tête « Devoir ».</summary>
        public const float Note = 30f;

        /// <summary>Moy/20 et Moy x — les deux seules à recevoir une valeur à 3 chiffres et 3 décimales (« 57,375 »).</summary>
        public const float Average = 35f;

        /// <summary>Coef, T.H, Rang — un ou deux caractères, l'en-tête fait la largeur.</summary>
        public const float Small = 25f;
    }

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
                // Classe passerelle / accélérée : mention juste sous le titre, AVANT le bloc d'identité —
                // elle qualifie tout le bulletin. Rien ne s'insère pour une classe ordinaire, dont la
                // mise en page reste exactement celle de la référence visuelle (AGENTS.md règle #12).
                if (reportCard.AcceleratedPathLabel is not null)
                {
                    column.Item().Element(ComposeAcceleratedMention);
                }
                column.Item().Element(ComposeIdentity);
                // Trois tableaux possibles, dans cet ordre de priorité :
                //   1. la GRILLE configurée par l'école (domaines → activités, barèmes propres), dès
                //      qu'elle existe pour le niveau de la classe — elle est le choix explicite de
                //      l'établissement et prime sur la déduction par cycle ;
                //   2. Primaire (/10) : tableau épuré sans coefficients/appréciations ;
                //   3. Secondaire (/20) : rendu d'origine, strictement inchangé.
                column.Item().PaddingTop(2).Element(
                    reportCard.EvaluationStructure is not null ? ComposeGradesTableApc
                    : IsPrimaire ? ComposeGradesTablePrimaire
                    : ComposeGradesTable);
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
    /// Mention du cursus accéléré (« Cursus Accéléré Passerelle — CI → CP »), centrée sous le titre.
    /// Composée UNIQUEMENT pour une classe passerelle : l'appelant ne l'insère pas autrement, et le
    /// bulletin d'une classe ordinaire ne gagne pas même une ligne vide.
    ///
    /// Sans encadré ni filet : le gabarit de la référence est fait de blocs bordés, en ajouter un de plus
    /// concurrencerait le titre. L'italique suffit à la lire comme une qualification du document.
    /// </summary>
    private void ComposeAcceleratedMention(IContainer container) =>
        container.PaddingTop(1.5f).AlignCenter()
            .Text(reportCard.AcceleratedPathLabel).Italic().Bold().FontSize(8f);

    /// <summary>
    /// Bloc d'identité, trois lignes EN TEXTE BRUT — sans encadré ni filet. La référence visuelle
    /// (docs/design-references/bulletin-reference.png, AGENTS.md règle #12) ne trace aucune bordure
    /// autour de ces trois lignes : un cadre de plus, juste sous le titre entre doubles filets et juste
    /// au-dessus du tableau bordé, empilait trois encadrements consécutifs et écrasait le titre.
    ///
    /// LIBELLÉS EN GRAS, valeurs en normal — même contraste que l'en-tête administratif ci-dessus, qui
    /// fait ressortir la structure du bloc sans la cerner d'un trait. Trois valeurs échappent à la règle
    /// et restent en gras, corps plus grand : Prénoms, Nom et Classe, mises en évidence sur la référence
    /// parce qu'elles identifient l'élève — c'est ce que le lecteur cherche en premier sur un bulletin.
    ///
    /// LES TROIS LIGNES PARTAGENT LES MÊMES COLONNES : c'est ce partage — pas un ajustement de largeurs
    /// au jugé — qui aligne « Nom », « Classe » et « Classe Redoublée » sur la même verticale, comme sur
    /// la référence. « Prénoms » fusionne les deux premières colonnes (ColumnSpan) pour lui laisser sa
    /// place habituelle, plus large.
    ///
    /// « Classe Redoublée » porte sa valeur en toutes lettres — « Oui » / « Non » d'après
    /// <see cref="ReportCardDto.IsRepeating"/> (feature F, Enrollment.IsRepeating) — et non plus une
    /// coche « [X] » / « [ ] » : dans une ligne désormais dépourvue de filets, une case vide ne se
    /// distinguait plus d'une case simplement pas encore remplie.
    /// </summary>
    private void ComposeIdentity(IContainer container)
    {
        var (prenoms, nom) = SplitFullName(reportCard.StudentFullName);

        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2f);
                columns.RelativeColumn(1.6f);
                columns.RelativeColumn(1.4f);
            });

            table.Cell().ColumnSpan(2).Element(Cell).Element(c => Field(c, "Prénoms", prenoms, emphasis: true));
            table.Cell().Element(Cell).Element(c => Field(c, "Nom", nom, emphasis: true));

            table.Cell().Element(Cell).Element(c => Field(c, "Né(e) le", FormatDate(reportCard.BirthDate)));
            table.Cell().Element(Cell).Element(c => Field(c, "à", reportCard.BirthPlace ?? ""));
            table.Cell().Element(Cell).Element(c => Field(c, "Classe", reportCard.ClassroomName, emphasis: true));

            table.Cell().Element(Cell).Element(c => Field(c, "Matricule", NoBreakText.NoBreak(reportCard.Matricule)));
            table.Cell().Element(Cell).Element(c => Field(c, "Nbre d'élèves", reportCard.ClassSize.ToString(CultureInfo.InvariantCulture)));
            table.Cell().Element(Cell).Element(c => Field(c, "Classe Redoublée", reportCard.IsRepeating ? "Oui" : "Non"));
        });

        static IContainer Cell(IContainer c) => c.PaddingVertical(1.5f);

        // Libellé gras + valeur normale, une seule définition pour les huit champs : sans elle, la mise
        // en forme dériverait d'une ligne à l'autre dès le premier champ ajouté.
        static void Field(IContainer container, string label, string value, bool emphasis = false) =>
            container.Text(text =>
            {
                text.Span($"{label} : ").Bold().FontSize(8.5f);

                var span = text.Span(value).FontSize(emphasis ? 9.5f : 8.5f);
                if (emphasis)
                {
                    span.Bold();
                }
            });
    }

    /// <summary>
    /// Tableau des disciplines du SECONDAIRE, les neuf colonnes de la référence visuelle
    /// (docs/design-references/bulletin-reference.png, AGENTS.md règle #12) :
    /// Disciplines | Devoir | Comp | Moy/20 | Coef | Moy x | T.H | Rang | Appréciations.
    ///
    /// LARGEURS : les sept colonnes de chiffres sont CONSTANTES (<see cref="NumericColumnWidths"/>),
    /// seules « Disciplines » et « Appréciations » se partagent le reste en relatif. Une note, un
    /// coefficient, un rang ont un gabarit fixe : leur donner une largeur relative les faisait respirer
    /// inutilement sur un bulletin à 3 matières et étranglait les deux colonnes de TEXTE — les seules
    /// dont le contenu varie vraiment — sur un bulletin à 12. Le reste (~175 pt sur les ~380 pt utiles
    /// d'une A5 portrait à marges de 7 mm) va aux libellés dans un rapport 25/20, comme sur la référence.
    /// </summary>
    private void ComposeGradesTable(IContainer container)
    {
        // Interligne ET corps, calculés d'un seul barème partagé par les trois tableaux : le bulletin
        // porte jusqu'à MaxRowCapacity disciplines sur une page A5 (voir RowMetricsFor). Jusqu'à 12
        // matières — le cas de JGK-G03 et de la quasi-totalité des bulletins — le rendu est inchangé.
        var (fontSize, rowPadding) = RowMetricsFor(reportCard.Subjects.Count);

        container.DefaultTextStyle(text => text.FontSize(fontSize)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(25f);                        // Disciplines — ~25 % de la largeur utile
                columns.ConstantColumn(NumericColumnWidths.Note);   // Devoir
                columns.ConstantColumn(NumericColumnWidths.Note);   // Composition
                columns.ConstantColumn(NumericColumnWidths.Average); // Moyenne — « 9,56 » et son en-tête « Moy/20 »
                columns.ConstantColumn(NumericColumnWidths.Small);  // Coefficient
                columns.ConstantColumn(NumericColumnWidths.Average); // Moyenne x Coef — « 57,375 », la valeur la plus large
                columns.ConstantColumn(NumericColumnWidths.Small);  // T.H
                columns.ConstantColumn(NumericColumnWidths.Small);  // Rang
                columns.RelativeColumn(20f);                        // Appréciations — ~20 % de la largeur utile
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("DISCIPLINES").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Devoir").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Comp").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text($"Moy/{reportCard.GradingScale}").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Coef").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Moy x").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("T.H").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold();
                header.Cell().Element(HeaderCell).Text("Appréciations").Bold();
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
            table.Cell().ColumnSpan(2).Element(TotalCell).AlignCenter().Text("Absences");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Absences));

            // Ligne Moyenne de la référence : moyenne générale, rang, retards, absences totales — dans
            // la MÊME table pour que les filets verticaux restent alignés avec le tableau des notes.
            table.Cell().Element(TotalCell).Text($"Moyenne : {FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale}").Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).AlignCenter().Text("Rang");
            table.Cell().Element(TotalCell).AlignCenter().Text(reportCard.GeneralRank.ToString()).Bold();
            table.Cell().ColumnSpan(2).Element(TotalCell).AlignCenter().Text("Retards");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Retards));

            // « Abs. Tot » tombe dans la colonne Rang, la plus étroite du tableau : corps réduit ET
            // rembourrage horizontal nul, sans quoi le libellé se coupe en deux lignes et fait gonfler
            // toute la rangée. Sa VALEUR, elle, dispose de la colonne Appréciations, la plus large.
            table.Cell().Element(TightTotalCell).AlignCenter().Text("Abs. Tot").FontSize(Math.Min(6f, fontSize));
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.TotalAbsences));
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(CellPadding);
        IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(rowPadding).PaddingHorizontal(CellPadding);
        static IContainer TotalCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f).PaddingHorizontal(CellPadding);
        // Même cellule, sans le moindre rembourrage horizontal — réservée au seul libellé « Abs. Tot ».
        static IContainer TightTotalCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2.5f);
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
        // Même barème d'interligne et de corps que le secondaire — une seule source de vérité pour
        // « combien de lignes tiennent sur une page » (voir RowMetricsFor).
        var (fontSize, rowPadding) = RowMetricsFor(reportCard.Subjects.Count);

        container.DefaultTextStyle(text => text.FontSize(fontSize)).Table(table =>
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
                header.Cell().Element(HeaderCell).Text("DISCIPLINES").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Devoir").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Comp").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text($"Moy/{reportCard.GradingScale}").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("T.H").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Rang").Bold();
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
            table.Cell().Element(TotalCell).AlignCenter().Text("Abs. Tot").FontSize(Math.Min(6.5f, fontSize));
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
    /// Tableau d'évaluation HIÉRARCHIQUE, celui des grilles par compétences du primaire (APC) : un
    /// domaine en première colonne, ses activités en seconde, puis Notes / Sur / Appréciations.
    ///
    /// Tout y vient de la configuration de l'école (<see cref="EvaluationStructureDto"/>) : les libellés
    /// des deux premières colonnes, le nombre de domaines, le nombre d'activités de chacun, et le barème
    /// « Sur » propre à chaque ligne. RIEN n'est codé en dur — trois écoles aux grilles différentes
    /// obtiennent trois tableaux différents du même code.
    ///
    /// La FUSION de la première colonne est calculée, pas devinée : <c>RowSpan(group.Lines.Count)</c>.
    /// Un domaine sans activité — une matière simple au milieu d'une grille par ailleurs hiérarchique —
    /// n'a rien à fusionner : son nom occupe alors les deux premières colonnes (ColumnSpan), et la ligne
    /// s'imprime normalement. C'est la rétrocompatibilité attendue pour le secondaire.
    ///
    /// Les cases de notes NON SAISIES restent vides, comme sur les modèles officiels : un bulletin
    /// imprimé en cours de trimestre montre la grille entière, pas seulement ce qui est déjà noté.
    /// </summary>
    private void ComposeGradesTableApc(IContainer container)
    {
        var structure = reportCard.EvaluationStructure!;

        // Même barème que les deux autres tableaux — mais compté en LIGNES DE GRILLE (une par activité),
        // pas en matières : c'est ce que le lecteur voit, et c'est ce qui remplit la page. Une grille de
        // 18 activités (le modèle du primaire : Lang & Com., Maths, DDM, EDD, EPSA, Langues étrangères)
        // tient donc sur une page, réserve comprise, sans qu'une grille de 5 lignes n'y flotte.
        var (fontSize, rowPadding) = RowMetricsFor(structure.LineCount);

        container.DefaultTextStyle(text => text.FontSize(fontSize)).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(2.1f);  // Domaines / Activités (libellé configurable)
                columns.RelativeColumn(2.6f);  // Activités / Contrôles (libellé configurable)
                columns.RelativeColumn(0.9f);  // Notes
                columns.RelativeColumn(0.7f);  // Sur
                columns.RelativeColumn(2.2f);  // Appréciations
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text(structure.Column1Header).Bold();
                header.Cell().Element(HeaderCell).Text(structure.Column2Header).Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Notes").Bold();
                header.Cell().Element(HeaderCell).AlignCenter().Text("Sur").Bold();
                header.Cell().Element(HeaderCell).Text("Appréciations").Bold();
            });

            foreach (var group in structure.Groups)
            {
                var hasActivities = group.Lines.Count > 0 && group.Lines[0].Label is not null;

                for (var i = 0; i < group.Lines.Count; i++)
                {
                    var line = group.Lines[i];

                    if (!hasActivities)
                    {
                        // Matière simple : le nom couvre les deux colonnes de libellés, aucune fusion
                        // verticale — exactement la ligne qu'imprimerait un tableau plat.
                        table.Cell().ColumnSpan(2).Element(BodyCell).Text(group.Name).Bold();
                    }
                    else if (i == 0)
                    {
                        // Le nom du domaine, une seule fois, fusionné sur la hauteur de ses activités.
                        table.Cell().RowSpan((uint)group.Lines.Count).Element(GroupCell)
                            .AlignMiddle().Text(group.Name).Bold();
                        table.Cell().Element(BodyCell).Text(line.Label);
                    }
                    else
                    {
                        // Lignes suivantes : QuestPDF place automatiquement la cellule après la zone
                        // occupée par le RowSpan ci-dessus — rien à réserver pour la première colonne.
                        table.Cell().Element(BodyCell).Text(line.Label);
                    }

                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatOptionalGrade(line.Score));
                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatGrade(line.MaxScore));
                    table.Cell().Element(BodyCell).Text(line.Appreciation ?? "");
                }
            }

            // Pied du tableau : moyenne générale et rang, puis l'assiduité du trimestre — les mêmes
            // informations que les deux autres tableaux, dans la MÊME table pour que les filets
            // verticaux restent alignés.
            table.Cell().ColumnSpan(3).Element(TotalCell)
                .Text($"Moyenne : {FormatGrade(reportCard.GeneralAverage)} /{reportCard.GradingScale}").Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text("Rang").Bold();
            table.Cell().Element(TotalCell).AlignCenter().Text(reportCard.GeneralRank.ToString()).Bold();

            // Les TROIS compteurs d'assiduité, comme les deux autres tableaux — cinq colonnes suffisent
            // tout juste, à condition que le dernier libellé porte sa valeur (« Abs. Tot : 2 ») plutôt
            // que d'exiger une sixième case qui n'existe pas.
            table.Cell().Element(TotalCell).AlignCenter().Text("Absences");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Absences));
            table.Cell().Element(TotalCell).AlignCenter().Text("Retards");
            table.Cell().Element(TotalCell).AlignCenter().Text(FormatOptionalCount(reportCard.Retards));
            table.Cell().Element(TotalCell).AlignCenter()
                .Text($"Abs. Tot : {FormatOptionalCount(reportCard.TotalAbsences)}");
        });

        static IContainer HeaderCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(3);
        IContainer BodyCell(IContainer c) =>
            c.Border(0.5f).BorderColor(Colors.Black).PaddingVertical(rowPadding).PaddingHorizontal(3);
        // La cellule fusionnée du domaine porte le filet ÉPAIS : c'est ce qui fait lire le groupe comme
        // un bloc sur les modèles officiels, là où les activités sont séparées d'un simple trait fin.
        static IContainer GroupCell(IContainer c) =>
            c.Border(RuleThickness).BorderColor(Colors.Black).PaddingVertical(2).PaddingHorizontal(3);
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
                // Signature réelle si le Directeur l'a téléversée (Paramètres → Établissement) ; sinon
                // simple espace réservé au-dessus du libellé, comme avant.
                if (directorSignature is not null)
                {
                    right.Item().AlignCenter().Height(24).Image(directorSignature).FitArea();
                }

                right.Item().PaddingTop(directorSignature is not null ? 1 : 0).AlignCenter().Text("Le Chef d'Établissement").Bold();

                right.Item().PaddingTop(3).AlignCenter().Height(46).Width(46).Element(stamp =>
                {
                    if (officialStamp is not null)
                    {
                        stamp.Image(officialStamp).FitArea();
                    }
                    else
                    {
                        stamp.Svg(stampCircleSvg);
                    }
                });
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
    ///
    /// La règle elle-même a été REMONTÉE dans <see cref="StudentNameSplitter"/> (Application) le jour où
    /// l'export Planète a eu besoin du même découpage : le nom imprimé sur le bulletin remis au tuteur
    /// et celui transmis au ministère doivent être découpés de façon identique. Cette méthode n'est plus
    /// qu'un alias — le comportement, et les tests qui l'exercent, sont inchangés.
    /// </summary>
    internal static (string Prenoms, string Nom) SplitFullName(string fullName) =>
        StudentNameSplitter.Split(fullName);

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
