using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Microsoft.Win32;
using System.IO;

namespace Bookkeeping.Wpf.Services;

/// <summary>
/// Handles export of reports to Excel (.xlsx) and PDF.
/// </summary>
public class ExportService
{
    static ExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    // ==================== EXCEL EXPORT ====================

    public void ExportDonationsToExcel(IEnumerable<DonationReportRow> rows, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Donations");
        ws.Cell(1, 1).Value = "Date";
        ws.Cell(1, 2).Value = "Sponsor";
        ws.Cell(1, 3).Value = "Category";
        ws.Cell(1, 4).Value = "Payment Method";
        ws.Cell(1, 5).Value = "Program";
        ws.Cell(1, 6).Value = "Amount";
        ws.Cell(1, 7).Value = "Receipt #";

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Sponsor;
            ws.Cell(row, 3).Value = r.Category;
            ws.Cell(row, 4).Value = r.PaymentMethod;
            ws.Cell(row, 5).Value = r.Program;
            ws.Cell(row, 6).Value = r.Amount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 7).Value = r.ReceiptNumber;
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
        ws.Cell(1, 3).Value = "Category";
        ws.Cell(1, 4).Value = "Payment Method";
        ws.Cell(1, 5).Value = "Program";
        ws.Cell(1, 6).Value = "Amount";
        ws.Cell(1, 7).Value = "Notes";

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Vendor;
            ws.Cell(row, 3).Value = r.Category;
            ws.Cell(row, 4).Value = r.PaymentMethod;
            ws.Cell(row, 5).Value = r.Program;
            ws.Cell(row, 6).Value = r.Amount;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 7).Value = r.Notes;
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportMonthlyToExcel(IEnumerable<MonthlyReportRow> rows, int year, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"Monthly {year}");
        ws.Cell(1, 1).Value = "Month";
        ws.Cell(1, 2).Value = "Income";
        ws.Cell(1, 3).Value = "Expenses";
        ws.Cell(1, 4).Value = "Net";
        ws.Cell(1, 5).Value = "Cash Balance";
        ws.Cell(1, 6).Value = "Bank Balance";

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Month;
            ws.Cell(row, 2).Value = r.Income;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 3).Value = r.Expenses;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 4).Value = r.Net;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 5).Value = r.CashBalance;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 6).Value = r.BankBalance;
            ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        // Add totals row
        ws.Cell(row, 1).Value = "TOTAL";
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 2).Value = rows.Sum(r => r.Income);
        ws.Cell(row, 3).Value = rows.Sum(r => r.Expenses);
        ws.Cell(row, 4).Value = rows.Sum(r => r.Net);
        ws.Row(row).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportProgramToExcel(IEnumerable<ProgramReportRow> rows, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Programs");
        ws.Cell(1, 1).Value = "Program";
        ws.Cell(1, 2).Value = "Income";
        ws.Cell(1, 3).Value = "Expenses";
        ws.Cell(1, 4).Value = "Balance";

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Program;
            ws.Cell(row, 2).Value = r.Income;
            ws.Cell(row, 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 3).Value = r.Expenses;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 4).Value = r.Balance;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportFundToExcel(IEnumerable<FundReportRow> rows, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Funds");
        ws.Cell(1, 1).Value = "Fund";
        ws.Cell(1, 2).Value = "Type";
        ws.Cell(1, 3).Value = "Income";
        ws.Cell(1, 4).Value = "Expenses";
        ws.Cell(1, 5).Value = "Net Balance";

        int row = 2;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.FundName;
            ws.Cell(row, 2).Value = r.IsRestricted ? "Restricted" : "Unrestricted";
            ws.Cell(row, 3).Value = r.Income;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 4).Value = r.Expenses;
            ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 5).Value = r.NetBalance;
            ws.Cell(row, 5).Style.NumberFormat.Format = "#,##0.00";
            row++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportSponsorToExcel(IEnumerable<SponsorReportRow> rows, string sponsorName, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add(sponsorName);
        ws.Cell(1, 1).Value = $"Sponsor: {sponsorName}";
        ws.Cell(2, 1).Value = "Date";
        ws.Cell(2, 2).Value = "Category";
        ws.Cell(2, 3).Value = "Amount";
        ws.Cell(2, 4).Value = "Receipt #";

        int row = 3;
        foreach (var r in rows)
        {
            ws.Cell(row, 1).Value = r.Date.ToString("yyyy-MM-dd");
            ws.Cell(row, 2).Value = r.Category;
            ws.Cell(row, 3).Value = r.Amount;
            ws.Cell(row, 3).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(row, 4).Value = r.ReceiptNumber;
            row++;
        }

        ws.Cell(row, 1).Value = "TOTAL";
        ws.Cell(row, 3).Value = rows.Sum(r => r.Amount);
        ws.Row(row).Style.Font.Bold = true;

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportIncomeStatementToExcel(IncomeStatementData data, string filePath)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add($"Stmt of Activities {data.Year}");

        ws.Cell(1, 1).Value = $"Statement of Activities — {data.Year}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "Total Revenue:";
        ws.Cell(2, 2).Value = data.TotalRevenue;
        ws.Cell(2, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(3, 1).Value = "Total Expenses:";
        ws.Cell(3, 2).Value = data.TotalExpenses;
        ws.Cell(3, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(4, 1).Value = "Change in Net Assets:";
        ws.Cell(4, 2).Value = data.NetIncome;
        ws.Cell(4, 2).Style.NumberFormat.Format = "#,##0.00";

        // Revenue breakdown
        int r = 6;
        ws.Cell(r, 1).Value = "Revenue by Category";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        foreach (var c in data.RevenueByCategory)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        // Expense breakdown
        r++;
        ws.Cell(r, 1).Value = "Expenses by Category";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        foreach (var c in data.ExpensesByCategory)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        // Monthly breakdown
        r += 2;
        ws.Cell(r, 1).Value = "Month";
        ws.Cell(r, 2).Value = "Revenue";
        ws.Cell(r, 3).Value = "Expenses";
        ws.Cell(r, 4).Value = "Net";
        ws.Row(r).Style.Font.Bold = true;
        r++;
        foreach (var m in data.MonthlyBreakdown)
        {
            ws.Cell(r, 1).Value = m.Month;
            ws.Cell(r, 2).Value = m.Revenue;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 3).Value = m.Expenses;
            ws.Cell(r, 3).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r, 4).Value = m.Net;
            ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        // Revenue by Fund
        r++;
        ws.Cell(r, 1).Value = "Revenue by Fund";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        foreach (var f in data.RevenueByFund)
        {
            ws.Cell(r, 1).Value = f.Category;
            ws.Cell(r, 2).Value = f.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        // Expenses by Fund
        r++;
        ws.Cell(r, 1).Value = "Expenses by Fund";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        foreach (var f in data.ExpensesByFund)
        {
            ws.Cell(r, 1).Value = f.Category;
            ws.Cell(r, 2).Value = f.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    public void ExportAnnualToExcel(AnnualReportData data, string filePath)
    {
        using var wb = new XLWorkbook();

        // Summary sheet
        var ws = wb.Worksheets.Add($"Annual {data.Year}");
        ws.Cell(1, 1).Value = $"Annual Report — {data.Year}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(2, 1).Value = "Total Income:";
        ws.Cell(2, 2).Value = data.TotalIncome;
        ws.Cell(2, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(3, 1).Value = "Total Expenses:";
        ws.Cell(3, 2).Value = data.TotalExpenses;
        ws.Cell(3, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(4, 1).Value = "Net:";
        ws.Cell(4, 2).Value = data.Net;
        ws.Cell(4, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(5, 1).Value = "Cash Balance:";
        ws.Cell(5, 2).Value = data.CashBalance;
        ws.Cell(5, 2).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(6, 1).Value = "Bank Balance:";
        ws.Cell(6, 2).Value = data.BankBalance;
        ws.Cell(6, 2).Style.NumberFormat.Format = "#,##0.00";

        // Income by category
        ws.Cell(8, 1).Value = "Income by Category";
        ws.Cell(8, 1).Style.Font.Bold = true;
        int r = 9;
        foreach (var c in data.IncomeByCategory)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        // Expenses by category
        r++;
        ws.Cell(r, 1).Value = "Expenses by Category";
        ws.Cell(r, 1).Style.Font.Bold = true;
        r++;
        foreach (var c in data.ExpensesByCategory)
        {
            ws.Cell(r, 1).Value = c.Category;
            ws.Cell(r, 2).Value = c.Amount;
            ws.Cell(r, 2).Style.NumberFormat.Format = "#,##0.00";
            r++;
        }

        ws.Columns().AdjustToContents();
        wb.SaveAs(filePath);
    }

    // ==================== PDF EXPORT ====================

    public void ExportToPdf(string title, List<string> headers, List<List<string>> dataRows, string filePath)
    {
        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Text(title).FontSize(18).Bold().AlignCenter();

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        for (int i = 0; i < headers.Count; i++)
                            columns.RelativeColumn();
                    });

                    // Header row
                    table.Header(header =>
                    {
                        foreach (var h in headers)
                            header.Cell().Background(Colors.Grey.Lighten2).Padding(4).Text(h).Bold();
                    });

                    // Data rows
                    foreach (var row in dataRows)
                    {
                        foreach (var cell in row)
                            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten3).Padding(4).Text(cell);
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Generated: ");
                    x.Span(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
                });
            });
        }).GeneratePdf(filePath);
    }

    public static string? ShowSaveDialog(string defaultName)
    {
        var dialog = new SaveFileDialog
        {
            FileName = defaultName,
            DefaultExt = ".xlsx",
            Filter = "Excel Files (*.xlsx)|*.xlsx|PDF Files (*.pdf)|*.pdf"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    // ==================== CHECK PRINTING ====================

    public void PrintCheck(string payee, decimal amount, string? memo, string organizationName, string filePath)
    {
        var amountWords = NumberToWords(amount);

        Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(8.5f, 3.5f, Unit.Inch); // Standard check size
                page.Margin(0.4f, Unit.Inch);

                page.Content().Column(col =>
                {
                    col.Spacing(8);

                    // Organization name top-left
                    col.Item().Text(organizationName).FontSize(14).Bold();

                    // Date top-right
                    col.Item().AlignRight().Text(DateTime.Now.ToString("MMMM dd, yyyy")).FontSize(11);

                    col.Item().Height(8);

                    // Pay to the order of
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(100).Text("Pay to the order of:").FontSize(10);
                        row.RelativeItem().BorderBottom(1).Text(payee).FontSize(12).Bold();
                    });

                    col.Item().Height(4);

                    // Amount in words
                    col.Item().BorderBottom(1).Text(amountWords).FontSize(10).Italic();

                    // Amount in numbers
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(40).Text("$").FontSize(14).Bold();
                        row.RelativeItem().AlignRight().Text(amount.ToString("N2")).FontSize(14).Bold();
                    });

                    col.Item().Height(4);

                    // Memo
                    col.Item().Row(row =>
                    {
                        row.ConstantItem(50).Text("Memo:").FontSize(9);
                        row.RelativeItem().BorderBottom(1).Text(memo ?? "").FontSize(10);
                    });

                    col.Item().Height(12);

                    // Signature line
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().BorderBottom(1);
                        row.ConstantItem(100);
                        row.RelativeItem().BorderBottom(1).Text("").FontSize(10);
                    });
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text("Authorized Signature").FontSize(8);
                        row.ConstantItem(100);
                        row.RelativeItem().AlignRight().Text("Date").FontSize(8);
                    });
                });
            });
        }).GeneratePdf(filePath);
    }

    private static string NumberToWords(decimal amount)
    {
        // Handle negative amounts defensively (shouldn't happen for checks, but be safe)
        if (amount < 0)
            return "Negative " + NumberToWords(-amount);

        var dollars = (long)Math.Floor(amount);
        var cents = (long)Math.Round((amount - dollars) * 100, MidpointRounding.AwayFromZero);

        var result = dollars switch
        {
            0 => "Zero",
            _ => NumberToWordsInternal(dollars)
        };

        result += " Dollars";
        if (cents > 0)
            result += $" and {cents:00}/100 Cents";

        return result;
    }

    private static string NumberToWordsInternal(long number)
    {
        if (number == 0) return "Zero";

        var ones = new[] { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
            "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen", "Eighteen", "Nineteen" };
        var tens = new[] { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

        if (number < 20) return ones[number];
        if (number < 100) return tens[number / 10] + (number % 10 > 0 ? "-" + ones[number % 10] : "");
        if (number < 1000) return ones[number / 100] + " Hundred" + (number % 100 > 0 ? " " + NumberToWordsInternal(number % 100) : "");
        if (number < 1000000) return NumberToWordsInternal(number / 1000) + " Thousand" + (number % 1000 > 0 ? " " + NumberToWordsInternal(number % 1000) : "");
        if (number < 1000000000) return NumberToWordsInternal(number / 1000000) + " Million" + (number % 1000000 > 0 ? " " + NumberToWordsInternal(number % 1000000) : "");

        return NumberToWordsInternal(number / 1000000000) + " Billion" + (number % 1000000000 > 0 ? " " + NumberToWordsInternal(number % 1000000000) : "");
    }

    public static string? ShowCheckSaveDialog(string payee)
    {
        var dialog = new SaveFileDialog
        {
            FileName = $"Check_{payee}_{DateTime.Now:yyyyMMdd}.pdf",
            DefaultExt = ".pdf",
            Filter = "PDF Files (*.pdf)|*.pdf"
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
