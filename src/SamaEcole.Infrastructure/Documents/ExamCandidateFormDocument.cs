using System.Globalization;
using SamaEcole.Application.Exams;
using SamaEcole.Infrastructure.Documents.Components;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Fiche(s) de candidature à un examen officiel (A4 portrait, charte <see cref="OfficialHeaderComponent"/>).
/// Une page par candidat : l'impression unitaire (GET .../candidate-form/pdf) et l'impression par lot
/// (POST .../candidate-forms/pdf) partagent donc le même document — une liste d'un seul élément dans
/// le premier cas, aucune duplication de gabarit dans le second (Volume 1 §22.5).
/// </summary>
public class ExamCandidateFormDocument(IReadOnlyList<ExamCandidateFormModel> candidates, byte[]? logo, Func<string, byte[]> qrCodeFactory)
    : IDocument
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = candidates.Count == 1
            ? $"Fiche de candidature {candidates[0].Reference}"
            : $"Fiches de candidature ({candidates.Count})",
        Author = candidates.Count > 0 ? candidates[0].SchoolName : string.Empty
    };

    public void Compose(IDocumentContainer container)
    {
        foreach (var candidate in candidates)
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
                        c, candidate.InspectionAcademie, candidate.InspectionEducationFormation,
                        "Établissement", candidate.SchoolName, logo));

                    column.Item().PaddingTop(24).AlignCenter()
                        .Text($"FICHE DE CANDIDATURE — {candidate.ExamType.ToUpperInvariant()}").Bold().FontSize(15).Underline();

                    if (!string.IsNullOrWhiteSpace(candidate.Series))
                    {
                        column.Item().PaddingTop(2).AlignCenter()
                            .Text($"Série {candidate.Series}").FontSize(10).FontColor(Colors.Grey.Darken2);
                    }

                    column.Item().PaddingTop(24).Element(c => ComposeCandidateBlock(c, candidate));
                    column.Item().PaddingTop(20).Element(c => ComposeExamBlock(c, candidate));
                    column.Item().PaddingTop(20).Element(c => ComposeCivilStatusBlock(c, candidate));

                    column.Item().PaddingTop(50).Element(c => ComposeSignature(c, candidate));

                    column.Item().PaddingTop(24).Element(c => OfficialHeaderComponent.ComposeAuthenticityFooter(
                        c, qrCodeFactory($"https://app.samaecole.sn/verify?ref={candidate.Reference}"), candidate.Reference));
                });
            });
        }
    }

    private static void ComposeCandidateBlock(IContainer container, ExamCandidateFormModel candidate)
    {
        container.Column(column =>
        {
            column.Item().Text("IDENTITÉ DU CANDIDAT").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(4).Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(inner =>
            {
                Field(inner, "Nom et prénom(s)", candidate.StudentFullName);
                Field(inner, "Matricule", candidate.StudentMatricule);
                Field(inner, "Date et lieu de naissance", $"{FormatDate(candidate.BirthDate)} à {candidate.BirthPlace}");
                Field(inner, "Sexe", candidate.Gender == "F" ? "Féminin" : "Masculin");
                Field(inner, "Classe", candidate.ClassroomName);
            });
        });
    }

    private static void ComposeExamBlock(IContainer container, ExamCandidateFormModel candidate)
    {
        container.Column(column =>
        {
            column.Item().Text("AFFECTATION D'EXAMEN").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(4).Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(inner =>
            {
                Field(inner, "Numéro de table", candidate.CandidateNumber ?? "En attente d'attribution");
                Field(inner, "Centre d'examen", candidate.ExamCenterName ?? "En attente d'attribution");
            });
        });
    }

    private static void ComposeCivilStatusBlock(IContainer container, ExamCandidateFormModel candidate)
    {
        container.Column(column =>
        {
            column.Item().Text("PIÈCE D'ÉTAT CIVIL").Bold().FontSize(9).FontColor(Colors.Grey.Darken3);

            column.Item().PaddingTop(4).Border(0.75f).BorderColor(Colors.Grey.Darken1).Padding(8).Column(inner =>
            {
                Field(inner, "Extrait de naissance",
                    candidate.BirthCertificatePresent ? "Fourni" : "Non fourni à ce jour");

                if (!string.IsNullOrWhiteSpace(candidate.BirthCertificateNumber))
                {
                    Field(inner, "N° d'enregistrement", candidate.BirthCertificateNumber);
                }
            });
        });
    }

    private static void ComposeSignature(IContainer container, ExamCandidateFormModel candidate)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Le Candidat / Le Représentant légal").FontSize(10);
                left.Item().PaddingTop(30).Text("[Signature]").FontSize(8).FontColor(Colors.Grey.Medium);
            });

            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Le Chef d'Établissement").FontSize(10);
                right.Item().PaddingTop(30).AlignRight().Text("[Signature et Cachet]").FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void Field(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingBottom(3).Text(text =>
        {
            text.Span($"{label} : ").Bold().FontSize(9.5f);
            text.Span(value).FontSize(9.5f);
        });

    private static string FormatDate(DateOnly date) => date.ToString("dd/MM/yyyy", FrenchCulture);
}
