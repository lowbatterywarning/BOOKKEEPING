using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.Win32;
using System.IO;

namespace Bookkeeping.Wpf.Services;

public class ExportService
{
    static ExportService() { QuestPDF.Settings.License = LicenseType.Community; }

    public void ExportDonationsToExcel(IEnumerable<DonationReportRow> rows, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Donations");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Sponsor";
        ws.Cell(1, 3).Value = "Program";
        ws.Cell(1, 4).Value = "Payment Method";
        ws.Cell(1, 5).Value = "Amount";
        ws.Cell(1, 6).Value = "Receipt #";
        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Sponsor;
            ws.Cell(row, 3).Value = r.Program;
            ws.Cell(row, 4).Value = r.PaymentMethod;
            ws.Cell(row, 5).Value = r.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Value = r.ReceiptNumber;
            row++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportExpensesToExcel(IEnumerable<ExpenseReportRow> rows, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Expenses");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Vendor";
        ws.Cell(1, 3).Value = "Program";
        ws.Cell(1, 4).Value = "Payment Method";
        ws.Cell(1, 5).Value = "Amount";
        ws.Cell(1, 6).Value = "Notes";
        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Vendor;
            ws.Cell(row, 3).Value = r.Program;
            ws.Cell(row, 4).Value = r.PaymentMethod;
            ws.Cell(row, 5).Value = r.Amount;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Value = r.Notes;
            row++;
        }
        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public static string? ShowCheckSaveDialog(string vendorName)
    {
        var dialog = new SaveFileDialog { Title = $"Save Check for {vendorName}", Filter = "PDF Files (*.pdf)|*.pdf", FileName = $"Check_{vendorName}_{DateTime.Now:yyyyMMdd}.pdf" };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void PrintCheck(string vendorName, decimal amount, string? notes, string orgName, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.Margin(30);
                page.Content().Column(col =>
                {
                    col.Item().Text("CHECK").FontSize(18).Bold();
                    col.Item().Text("");
                    col.Item().Text($"Pay to: {vendorName}").FontSize(14);
                    col.Item().Text($"Amount: {amount:C}").FontSize(14).Bold();
                    if (!string.IsNullOrEmpty(notes)) col.Item().Text($"Memo: {notes}").FontSize(11);
                    col.Item().Text("");
                    col.Item().Text(orgName).FontSize(11);
                    col.Item().Text(DateTime.Now.ToString("MMMM dd, yyyy")).FontSize(11);
                });
            });
        }).GeneratePdf(filePath);
    }
}
