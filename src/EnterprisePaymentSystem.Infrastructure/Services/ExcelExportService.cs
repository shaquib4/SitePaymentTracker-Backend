using ClosedXML.Excel;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class ExcelExportService : IExcelExportService
{
    public byte[] GenerateExpensesExcel(IReadOnlyList<PaymentResponseDto> payments, string tenantName, int year)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"Expenses {year}");

        // Title Block
        worksheet.Cell("A1").Value = $"{tenantName} - Payment & Expense Report ({year})";
        worksheet.Range("A1:K1").Merge();
        worksheet.Cell("A1").Style.Font.Bold = true;
        worksheet.Cell("A1").Style.Font.FontSize = 14;
        worksheet.Cell("A1").Style.Font.FontColor = XLColor.White;
        worksheet.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1e1b4b");
        worksheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Row(1).Height = 28;

        // Subtitle / Generated metadata
        worksheet.Cell("A2").Value = $"Generated on: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | Total Records: {payments.Count}";
        worksheet.Range("A2:K2").Merge();
        worksheet.Cell("A2").Style.Font.Italic = true;
        worksheet.Cell("A2").Style.Font.FontSize = 9;
        worksheet.Cell("A2").Style.Font.FontColor = XLColor.White;
        worksheet.Cell("A2").Style.Fill.BackgroundColor = XLColor.FromHtml("#312e81");
        worksheet.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Row(2).Height = 18;

        // Header Row
        var headers = new[]
        {
            "Request ID",
            "Submitted By",
            "Email",
            "Title",
            "Category",
            "Amount (₹)",
            "Currency",
            "Start Date",
            "End Date",
            "Status",
            "Invoice #"
        };

        for (int i = 0; i < headers.Length; i++)
        {
            var cell = worksheet.Cell(4, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#4f46e5");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        worksheet.Row(4).Height = 22;

        // Data Rows
        int currentRow = 5;
        foreach (var p in payments)
        {
            worksheet.Cell(currentRow, 1).Value = p.Id.ToString().Substring(0, 8) + "...";
            worksheet.Cell(currentRow, 2).Value = p.SubmitterName;
            worksheet.Cell(currentRow, 3).Value = p.SubmitterEmail;
            worksheet.Cell(currentRow, 4).Value = p.Title;
            worksheet.Cell(currentRow, 5).Value = p.Category;

            // Strict financial formatting
            var amountCell = worksheet.Cell(currentRow, 6);
            amountCell.Value = p.Amount;
            amountCell.Style.NumberFormat.Format = "#,##0.00";
            amountCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 7).Value = p.Currency;
            worksheet.Cell(currentRow, 8).Value = p.StartDate.ToString("yyyy-MM-dd");
            worksheet.Cell(currentRow, 9).Value = p.EndDate.ToString("yyyy-MM-dd");

            var statusCell = worksheet.Cell(currentRow, 10);
            statusCell.Value = p.StatusName;
            statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ApplyStatusColor(statusCell, p.StatusName);

            worksheet.Cell(currentRow, 11).Value = p.InvoiceNumber ?? "-";

            // Light zebra striping
            if (currentRow % 2 == 0)
            {
                worksheet.Range(currentRow, 1, currentRow, 11).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
            }

            for (int col = 1; col <= 11; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            currentRow++;
        }

        // Summary Formula Row
        if (payments.Count > 0)
        {
            worksheet.Cell(currentRow, 5).Value = "Total Approved/Disbursed:";
            worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 5).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            var sumCell = worksheet.Cell(currentRow, 6);
            sumCell.FormulaA1 = $"SUM(F5:F{currentRow - 1})";
            sumCell.Style.Font.Bold = true;
            sumCell.Style.NumberFormat.Format = "#,##0.00";
            sumCell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            sumCell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            sumCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#e0e7ff");

            worksheet.Range(currentRow, 1, currentRow, 11).Style.Font.Bold = true;
        }

        worksheet.Columns().AdjustToContents(10, 45);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    private static void ApplyStatusColor(IXLCell cell, string status)
    {
        switch (status)
        {
            case "Approved":
                cell.Style.Font.FontColor = XLColor.FromHtml("#166534");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");
                break;
            case "Paid":
                cell.Style.Font.FontColor = XLColor.FromHtml("#1e40af");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
                break;
            case "PendingApproval":
                cell.Style.Font.FontColor = XLColor.FromHtml("#854d0e");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef9c3");
                break;
            case "Rejected":
                cell.Style.Font.FontColor = XLColor.FromHtml("#991b1b");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
                break;
        }
    }
}
