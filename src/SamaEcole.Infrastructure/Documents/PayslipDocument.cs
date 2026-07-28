using System.Globalization;
using SamaEcole.Application.Finance.Queries.GetPayslip;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SamaEcole.Infrastructure.Documents;

/// <summary>
/// Bulletin de Paie (A4 portrait) — détail des cotisations sociales sénégalaises (IPRES, CSS,
/// VRS/CFCE, BRS) et du net à payer, remis mensuellement à l'employé. Contrairement au reçu ou au
/// certificat, ce n'est pas un document destiné à l'élève/tuteur mais un document RH interne : pas de
/// mention du formalisme M.E.N., juste l'identité de l'établissement comme employeur (même en-tête
/// que le reçu de paiement).
/// </summary>
public class PayslipDocument(PayslipDto payslip, byte[]? logo) : IDocument
{
    private static readonly CultureInfo French = new("fr-FR");

    public DocumentMetadata GetMetadata() => new()
    {
        Title = $"Bulletin de paie {payslip.PayslipNumber}",
        Author = payslip.SchoolName
    };

    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(20, Unit.Millimetre);
            page.DefaultTextStyle(text => text.FontSize(9).FontColor(Colors.Black));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);

                column.Item().PaddingTop(16).AlignCenter()
                    .Text("BULLETIN DE PAIE").Bold().FontSize(15);
                column.Item().AlignCenter().Text(PeriodLabel()).FontSize(10).FontColor(Colors.Grey.Darken2);
                column.Item().AlignCenter()
                    .Text($"N° {NoBreakText.NoBreak(payslip.PayslipNumber)}").FontSize(8).FontColor(Colors.Grey.Darken1);

                column.Item().PaddingTop(16).Element(ComposeEmployeeBlock);
                column.Item().PaddingTop(14).Element(ComposeEarningsTable);
                column.Item().PaddingTop(6).Element(ComposeNetPayBlock);
                column.Item().PaddingTop(30).Element(ComposeSignatures);
            });
        });
    }

    private void ComposeHeader(IContainer container)
    {
        container.BorderBottom(1).BorderColor(Colors.Grey.Darken1).PaddingBottom(6).Row(row =>
        {
            row.RelativeItem().Column(header =>
            {
                header.Item().Text(payslip.SchoolName.ToUpperInvariant()).Bold().FontSize(13);

                if (!string.IsNullOrWhiteSpace(payslip.SchoolAddress))
                {
                    header.Item().Text(payslip.SchoolAddress).FontSize(7).FontColor(Colors.Grey.Darken2);
                }

                if (!string.IsNullOrWhiteSpace(payslip.SchoolNinea))
                {
                    header.Item().Text($"NINEA : {payslip.SchoolNinea}").FontSize(7).FontColor(Colors.Grey.Darken2);
                }
            });

            if (logo is not null)
            {
                row.ConstantItem(60).MaxHeight(42).AlignRight().Image(logo).FitArea();
            }
        });
    }

    private void ComposeEmployeeBlock(IContainer container)
    {
        container.Border(0.75f).BorderColor(Colors.Grey.Lighten1).Padding(8).Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                InfoRow(left, "Employé(e)", payslip.EmployeeFullName);
                InfoRow(left, "Qualité", payslip.EmployeeRole);
            });
            row.RelativeItem().Column(right =>
            {
                InfoRow(right, "Type de contrat", ContractTypeLabel());
                if (payslip.HoursWorked > 0)
                {
                    InfoRow(right, "Heures travaillées", payslip.HoursWorked.ToString("0.##", CultureInfo.InvariantCulture));
                }
            });
        });
    }

    private static void InfoRow(ColumnDescriptor column, string label, string value) =>
        column.Item().PaddingVertical(1).Row(row =>
        {
            row.ConstantItem(100).Text($"{label} :").FontColor(Colors.Grey.Darken2);
            row.RelativeItem().Text(value).SemiBold();
        });

    private void ComposeEarningsTable(IContainer container)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn(4);
                columns.RelativeColumn(2);
                columns.RelativeColumn(2);
            });

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("Élément").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Part salariale").Bold();
                header.Cell().Element(HeaderCell).AlignRight().Text("Part patronale").Bold();
            });

            Row("Salaire brut", payslip.GrossSalary, null);
            Row("Prime de transport", payslip.TransportAllowance, null);
            Row("IPRES (retraite)", -payslip.IpresEmployee, payslip.IpresEmployer);
            Row("CSS (prestations familiales)", null, payslip.CssEmployer);
            Row("VRS / CFCE", null, payslip.Vrs);
            Row("BRS (retenue à la source)", -payslip.Brs, null);

            void Row(string label, decimal? employee, decimal? employer)
            {
                table.Cell().Element(BodyCell).Text(label);
                table.Cell().Element(BodyCell).AlignRight().Text(employee.HasValue ? FormatMoney(employee.Value) : "—");
                table.Cell().Element(BodyCell).AlignRight().Text(employer.HasValue ? FormatMoney(employer.Value) : "—");
            }
        });
    }

    private void ComposeNetPayBlock(IContainer container)
    {
        container.Background(Colors.Grey.Lighten4).Padding(10).Row(row =>
        {
            row.RelativeItem().Text("NET À PAYER").Bold().FontSize(11);
            row.RelativeItem().AlignRight().Text(FormatMoney(payslip.NetSalary)).Bold().FontSize(13);
        });
    }

    private void ComposeSignatures(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().Column(left =>
            {
                left.Item().Text("Signature de l'Employeur").Italic().FontSize(9);
                left.Item().PaddingTop(30).Text("________________________").FontSize(9);
            });
            row.RelativeItem().AlignRight().Column(right =>
            {
                right.Item().AlignRight().Text("Signature de l'Employé(e)").Italic().FontSize(9);
                right.Item().PaddingTop(30).AlignRight().Text("________________________").FontSize(9);
            });
        });
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.DefaultTextStyle(x => x.FontSize(8).FontColor(Colors.Grey.Darken3))
            .PaddingVertical(4).BorderBottom(1).BorderColor(Colors.Grey.Darken1);

    private static IContainer BodyCell(IContainer container) =>
        container.PaddingVertical(4).BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2);

    private string PeriodLabel() =>
        new DateTime(payslip.Year, payslip.Month, 1).ToString("MMMM yyyy", French);

    private string ContractTypeLabel() =>
        payslip.ContractType == "Vacataire" ? "Vacataire (horaire)" : "Permanent";

    private static string FormatMoney(decimal amount) =>
        amount.ToString("#,##0", CultureInfo.InvariantCulture).Replace(",", " ") + " FCFA";
}
