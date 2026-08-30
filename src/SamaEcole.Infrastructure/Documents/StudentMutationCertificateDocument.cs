using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SamaEcole.Application.StateIntegration;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Certificat de mutation — A4 portrait, UNE page (Volume 1 §23.5, ticket JGK-M06).
///
/// Pièce officielle remise au tuteur et présentée à l'école d'accueil. Elle atteste de trois choses,
/// et de rien d'autre : que l'élève était inscrit, dans quelle classe, et qu'il quitte l'établissement.
///
/// CE QU'ELLE NE PORTE PAS, délibérément :
///   • Aucun montant, aucun solde. La mention de régularité financière est une PHRASE, pas un chiffre :
///     un certificat qui afficherait « reste dû : 45 000 FCFA » deviendrait un instrument de pression
///     sur une famille qui déménage, et circulerait entre des mains qui n'ont pas à connaître ses
///     finances.
///   • Aucune note, aucune moyenne. Le bulletin et le livret de compétences existent pour cela et se
///     remettent séparément.
///
/// Le QR encode une URL de VÉRIFICATION, jamais l'état civil : un QR photographié sur un bureau est
/// lisible par n'importe qui.
/// </summary>
public class StudentMutationCertificateDocument(
    StudentMutationCertificateModel model,
    byte[]? qrCode) : IDocument
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Certificat de mutation — {model.StudentFullName} — {model.CertificateNumber}",
        Author = model.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(1.6f, Unit.Centimetre);
            page.DefaultTextStyle(text => text.FontSize(10).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);
                column.Item().PaddingTop(14).Element(ComposeTitle);
                column.Item().PaddingTop(14).Element(ComposeIdentity);
                column.Item().PaddingTop(12).Element(ComposeDeclaration);
                column.Item().PaddingTop(12).Element(ComposeMutation);
                column.Item().PaddingTop(12).Element(ComposeFinancialMention);
            });

            page.Footer().Element(ComposeFooter);
        });
    }

    private void ComposeHeader(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Black).PaddingBottom(6).Column(column =>
        {
            column.Item().Text("RÉPUBLIQUE DU SÉNÉGAL").Bold().FontSize(9);
            column.Item().Text("Ministère de l'Éducation nationale").FontSize(8.5f);

            // Chaque mention absente est simplement OMISE — jamais un séparateur orphelin ni une ligne
            // légendée vide. Convention commune à toutes les pièces officielles du projet.
            var administration = string.Join(
                "  ·  ",
                new[] { model.InspectionAcademie, model.InspectionEducationFormation }
                    .Where(v => !string.IsNullOrWhiteSpace(v)));

            if (administration.Length > 0)
            {
                column.Item().PaddingTop(1).Text(administration).FontSize(8.5f).FontColor(Colors.Grey.Darken2);
            }

            column.Item().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Item().Text(model.SchoolName.ToUpperInvariant()).Bold().FontSize(12);

                    var contact = string.Join(
                        "  ·  ",
                        new[] { model.Address, model.Phone, model.Email }
                            .Where(v => !string.IsNullOrWhiteSpace(v)));

                    if (contact.Length > 0)
                    {
                        left.Item().Text(contact).FontSize(8).FontColor(Colors.Grey.Darken2);
                    }

                    if (!string.IsNullOrWhiteSpace(model.MinistryAuthorizationNumber))
                    {
                        left.Item().Text($"Autorisation n° {model.MinistryAuthorizationNumber}")
                            .FontSize(8).FontColor(Colors.Grey.Darken2);
                    }

                    if (!string.IsNullOrWhiteSpace(model.NationalSchoolCode))
                    {
                        left.Item().Text($"Code établissement : {model.NationalSchoolCode}")
                            .FontSize(8).FontColor(Colors.Grey.Darken2);
                    }
                });

                row.ConstantItem(90).AlignRight().Column(right =>
                {
                    right.Item().AlignRight().Text($"N° {model.CertificateNumber}").Bold().FontSize(9);
                    right.Item().AlignRight()
                        .Text(model.IssuedOn.ToString("dd/MM/yyyy", FrenchCulture)).FontSize(9);
                });
            });
        });

    private void ComposeTitle(IContainer container) =>
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("CERTIFICAT DE MUTATION").Bold().FontSize(16);
            column.Item().PaddingTop(2).AlignCenter()
                .Text($"Année scolaire {model.SchoolYearLabel}").FontSize(10).FontColor(Colors.Grey.Darken2);
        });

    private void ComposeIdentity(IContainer container) =>
        container.Border(0.75f).BorderColor(Colors.Black).Background(Colors.Grey.Lighten5)
            .Padding(8).Column(column =>
            {
                column.Item().Row(row =>
                {
                    row.RelativeItem(2).Element(c => Field(c, "Élève", model.StudentFullName, strong: true));
                    row.RelativeItem().Element(c => Field(c, "Matricule", model.Matricule));
                });

                column.Item().PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Element(c => Field(c, "Né(e) le",
                        model.BirthDate.ToString("dd/MM/yyyy", FrenchCulture)));
                    row.RelativeItem().Element(c => Field(c, "À", model.BirthPlace));
                    row.RelativeItem().Element(c => Field(c, "Sexe", model.Gender));
                    row.RelativeItem().Element(c => Field(c, "Classe quittée", model.ClassroomName));
                });

                column.Item().PaddingTop(5).Element(ComposeIenField);
            });

    /// <summary>
    /// L'IEN, avec la mention « (provisoire) » quand il est fabriqué. C'est la donnée que l'école
    /// d'accueil recopiera dans SES fichiers, puis transmettra au ministère à son tour : lui laisser
    /// croire qu'un numéro provisoire est officiel propagerait notre numéro de secours dans le fichier
    /// national. Le signaler ici est le seul endroit où la chaîne peut être coupée.
    /// </summary>
    private void ComposeIenField(IContainer container)
    {
        if (model.IenNumber is not { Length: > 0 } ien)
        {
            container.Element(c => Field(c, "Identifiant National de l'Élève (IEN)",
                "Non attribué à ce jour"));
            return;
        }

        container.Text(text =>
        {
            text.Span("Identifiant National de l'Élève (IEN) : ").FontSize(8.5f).FontColor(Colors.Grey.Darken2);
            text.Span(ien).Bold().FontSize(10);

            if (model.IsIenProvisional)
            {
                text.Span("   (provisoire — numéro interne, non délivré par le ministère)")
                    .Italic().FontSize(8).FontColor(Colors.Grey.Darken2);
            }
        });
    }

    private void ComposeDeclaration(IContainer container) =>
        container.BorderLeft(2).BorderColor(Colors.Grey.Darken1).PaddingLeft(8).Text(text =>
        {
            text.Justify();
            text.Span("Je soussigné(e), Chef de l'établissement ");
            text.Span(model.SchoolName).Bold();
            text.Span(", certifie que l'élève ");
            text.Span(model.StudentFullName).Bold();
            text.Span($" (matricule {model.Matricule}), régulièrement inscrit(e) en classe de ");
            text.Span(model.ClassroomName).Bold();
            text.Span($" au titre de l'année scolaire {model.SchoolYearLabel}, ");
            text.Span("quitte notre établissement").Bold();
            text.Span(" et est autorisé(e) à poursuivre sa scolarité dans tout autre établissement.");
        });

    private void ComposeMutation(IContainer container) =>
        container.Column(column =>
        {
            column.Item().Text("MOTIF ET DESTINATION").Bold().FontSize(9)
                .FontColor(Colors.Grey.Darken2);

            column.Item().PaddingTop(4).Border(0.5f).BorderColor(Colors.Grey.Medium)
                .Padding(7).Column(inner =>
                {
                    inner.Item().Element(c => Field(c, "Motif de la mutation", model.ReasonLabel));

                    // Le détail ne s'imprime que s'il apporte quelque chose de plus que le libellé —
                    // sinon la pièce répéterait deux fois la même information sur deux lignes.
                    if (!string.IsNullOrWhiteSpace(model.ReasonDetails)
                        && model.ReasonDetails != model.ReasonLabel)
                    {
                        inner.Item().PaddingTop(4).Element(c => Field(c, "Précision", model.ReasonDetails));
                    }

                    inner.Item().PaddingTop(4).Row(row =>
                    {
                        // « Non précisé » plutôt qu'une ligne omise : sur une pièce officielle,
                        // l'absence de destination est une information (le tuteur n'a pas encore
                        // choisi), et le certificat reste valable — voir l'entité.
                        row.RelativeItem().Element(c => Field(c, "Établissement de destination",
                            model.DestinationSchoolName ?? "Non précisé"));
                        row.RelativeItem().Element(c => Field(c, "Localité",
                            model.DestinationCity ?? "Non précisée"));
                    });
                });
        });

    /// <summary>
    /// Mention de régularité financière — une PHRASE, jamais un montant (voir la remarque de classe).
    /// Elle s'imprime dans les deux cas : dire que l'élève est à jour a de la valeur pour l'école
    /// d'accueil, et taire la situation inverse ferait de l'omission un mensonge par défaut.
    /// </summary>
    private void ComposeFinancialMention(IContainer container) =>
        container.Border(0.5f).BorderColor(Colors.Grey.Medium).Padding(7).Text(text =>
        {
            text.Span("Situation financière au jour de la délivrance : ").SemiBold().FontSize(9);

            text.Span(model.WasFinanciallyClear
                    ? "l'élève était à jour de ses frais de scolarité."
                    : "des frais de scolarité restaient dus à la date de délivrance. "
                      + "Cette mention n'affecte pas la validité du présent certificat.")
                .FontSize(9);
        });

    private void ComposeFooter(IContainer container) =>
        container.PaddingTop(10).Column(column =>
        {
            column.Item().Row(row =>
            {
                // QR de vérification à gauche, avec son libellé : un QR sans légende n'est pas scanné.
                row.ConstantItem(110).Column(left =>
                {
                    if (qrCode is not null)
                    {
                        left.Item().Width(72).Height(72).Image(qrCode).FitArea();
                    }

                    left.Item().PaddingTop(2).Text("Vérifier l'authenticité").Bold().FontSize(7);
                    left.Item().Text(model.VerificationUrl).FontSize(5.5f).FontColor(Colors.Grey.Darken1);
                });

                row.RelativeItem().AlignRight().Column(right =>
                {
                    var city = string.IsNullOrWhiteSpace(model.City) ? "" : $"{model.City}, ";
                    right.Item().AlignRight()
                        .Text($"Fait à {city}le {model.IssuedOn.ToString("dd MMMM yyyy", FrenchCulture)}")
                        .Italic().FontSize(9);

                    if (model.DirectorSignature is not null)
                    {
                        right.Item().PaddingTop(4).AlignRight().Height(34)
                            .Image(model.DirectorSignature).FitArea();
                    }

                    right.Item().PaddingTop(model.DirectorSignature is not null ? 1 : 22)
                        .AlignRight().Text("LE CHEF D'ÉTABLISSEMENT").Bold().FontSize(9);

                    if (model.OfficialStamp is not null)
                    {
                        right.Item().PaddingTop(3).AlignRight().Height(56).Width(56)
                            .Image(model.OfficialStamp).FitArea();
                    }
                });
            });

            column.Item().PaddingTop(8).BorderTop(0.5f).BorderColor(Colors.Grey.Medium).PaddingTop(3)
                .Text("Ce certificat atteste de la scolarisation de l'élève au sein de l'établissement "
                      + "émetteur et de son départ. Il ne vaut ni bulletin de notes, ni quitus comptable.")
                .Italic().FontSize(7).FontColor(Colors.Grey.Darken2);
        });

    /// <summary>Étiquette au-dessus de sa valeur — le gabarit de champ commun aux pièces du projet.</summary>
    private static void Field(IContainer container, string label, string? value, bool strong = false) =>
        container.Column(column =>
        {
            column.Item().Text(label.ToUpperInvariant()).FontSize(6.5f).FontColor(Colors.Grey.Darken2);

            var text = column.Item().Text(string.IsNullOrWhiteSpace(value) ? "—" : value);
            if (strong)
            {
                text.Bold().FontSize(12);
            }
            else
            {
                text.FontSize(10);
            }
        });
}
