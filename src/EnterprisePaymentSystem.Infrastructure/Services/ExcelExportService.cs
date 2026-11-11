using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class ExcelExportService : IExcelExportService
{
    public byte[] GenerateExpensesExcel(IReadOnlyList<PaymentResponseDto> payments, string tenantName, int year)
        => GenerateAuditExpensesExcel(payments, tenantName, year, $"FY {year}", "All Records");

    public byte[] GenerateAuditExpensesExcel(
        IReadOnlyList<PaymentResponseDto> payments,
        string tenantName,
        int year,
        string periodLabel,
        string filterSummary)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add($"Expenses {year}");

        worksheet.Cell("A1").Value = $"{tenantName} - Payment & Expense Audit Ledger ({year})";
        worksheet.Range("A1:N1").Merge();
        worksheet.Cell("A1").Style.Font.Bold = true;
        worksheet.Cell("A1").Style.Font.FontSize = 14;
        worksheet.Cell("A1").Style.Font.FontColor = XLColor.White;
        worksheet.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1e1b4b");
        worksheet.Cell("A1").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Row(1).Height = 28;

        var normalizedFilter = string.IsNullOrWhiteSpace(filterSummary) ? "All Records" : filterSummary;
        worksheet.Cell("A2").Value =
            $"Period: {periodLabel} | Filters: {normalizedFilter} | Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC | Total Records: {payments.Count}";
        worksheet.Range("A2:N2").Merge();
        worksheet.Cell("A2").Style.Font.Italic = true;
        worksheet.Cell("A2").Style.Font.FontSize = 9;
        worksheet.Cell("A2").Style.Font.FontColor = XLColor.White;
        worksheet.Cell("A2").Style.Fill.BackgroundColor = XLColor.FromHtml("#312e81");
        worksheet.Cell("A2").Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Row(2).Height = 18;

        var headers = GetHeaders();
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

        int currentRow = 5;
        foreach (var p in payments)
        {
            worksheet.Cell(currentRow, 1).Value = p.Id.ToString()[..8] + "...";
            worksheet.Cell(currentRow, 2).Value = p.SubmitterName;
            worksheet.Cell(currentRow, 3).Value = p.SubmitterEmail;
            worksheet.Cell(currentRow, 4).Value = string.IsNullOrWhiteSpace(p.SiteName) ? "Head Office / General" : p.SiteName;
            worksheet.Cell(currentRow, 5).Value = p.Title;
            worksheet.Cell(currentRow, 6).Value = string.IsNullOrWhiteSpace(p.Description) ? "-" : p.Description;
            worksheet.Cell(currentRow, 7).Value = p.Category;

            var reqAmountCell = worksheet.Cell(currentRow, 8);
            reqAmountCell.Value = p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount;
            reqAmountCell.Style.NumberFormat.Format = "#,##0.00";
            reqAmountCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            var apprAmountCell = worksheet.Cell(currentRow, 9);
            if (p.ApprovedAmount.HasValue)
            {
                apprAmountCell.Value = p.ApprovedAmount.Value;
                apprAmountCell.Style.NumberFormat.Format = "#,##0.00";
            }
            else
            {
                apprAmountCell.Value = "-";
            }
            apprAmountCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            worksheet.Cell(currentRow, 10).Value = p.Currency;
            worksheet.Cell(currentRow, 11).Value = p.StartDate.ToString("yyyy-MM-dd");
            worksheet.Cell(currentRow, 12).Value = p.EndDate.ToString("yyyy-MM-dd");

            var statusCell = worksheet.Cell(currentRow, 13);
            statusCell.Value = p.StatusName;
            statusCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ApplyStatusColor(statusCell, p.StatusName);

            worksheet.Cell(currentRow, 14).Value = p.InvoiceNumber ?? "-";

            if (currentRow % 2 == 0)
            {
                worksheet.Range(currentRow, 1, currentRow, 14).Style.Fill.BackgroundColor = XLColor.FromHtml("#f8fafc");
            }

            for (int col = 1; col <= 14; col++)
            {
                worksheet.Cell(currentRow, col).Style.Border.OutsideBorder = XLBorderStyleValues.Hair;
            }

            currentRow++;
        }

        if (payments.Count > 0)
        {
            worksheet.Cell(currentRow, 7).Value = "Totals:";
            worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
            worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

            var reqSumCell = worksheet.Cell(currentRow, 8);
            reqSumCell.FormulaA1 = $"SUM(H5:H{currentRow - 1})";
            reqSumCell.Style.Font.Bold = true;
            reqSumCell.Style.NumberFormat.Format = "#,##0.00";
            reqSumCell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            reqSumCell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            reqSumCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#e0e7ff");

            var apprSumCell = worksheet.Cell(currentRow, 9);
            apprSumCell.FormulaA1 = $"SUM(I5:I{currentRow - 1})";
            apprSumCell.Style.Font.Bold = true;
            apprSumCell.Style.NumberFormat.Format = "#,##0.00";
            apprSumCell.Style.Border.TopBorder = XLBorderStyleValues.Thin;
            apprSumCell.Style.Border.BottomBorder = XLBorderStyleValues.Double;
            apprSumCell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");

            worksheet.Range(currentRow, 1, currentRow, 14).Style.Font.Bold = true;
        }

        worksheet.Columns().AdjustToContents(10, 45);

        using var memoryStream = new MemoryStream();
        workbook.SaveAs(memoryStream);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// Forward-only SAX OpenXmlWriter streaming generator designed for 100,000+ rows
    /// with constant low memory footprint (no per-cell ClosedXML DOM allocation).
    /// </summary>
    public async Task<byte[]> GenerateChunkedAuditExpensesExcelAsync(
        IAsyncEnumerable<IReadOnlyList<AuditExpenseRowDto>> rowChunks,
        string tenantName,
        int year,
        string periodLabel,
        string filterSummary,
        CancellationToken ct = default)
    {
        var tempFilePath = Path.Combine(Path.GetTempPath(), $"ke_audit_{Guid.NewGuid():N}.xlsx");

        try
        {
            using (var document = SpreadsheetDocument.Create(tempFilePath, SpreadsheetDocumentType.Workbook))
            {
                var workbookPart = document.AddWorkbookPart();
                workbookPart.Workbook = new Workbook();

                var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
                stylesPart.Stylesheet = CreateStreamingStylesheet();
                stylesPart.Stylesheet.Save();

                var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
                var sheets = workbookPart.Workbook.AppendChild(new Sheets());
                sheets.Append(new Sheet
                {
                    Id = workbookPart.GetIdOfPart(worksheetPart),
                    SheetId = 1,
                    Name = $"Expenses {year}"
                });

                using var writer = OpenXmlWriter.Create(worksheetPart);
                writer.WriteStartElement(new Worksheet());

                // Fixed practical column widths so Excel doesn't need AdjustToContents across 100k+ rows
                writer.WriteStartElement(new Columns());
                var widths = new double[] { 14, 22, 28, 24, 30, 38, 22, 18, 18, 10, 13, 13, 16, 16 };
                for (uint i = 0; i < widths.Length; i++)
                {
                    writer.WriteElement(new Column
                    {
                        Min = i + 1,
                        Max = i + 1,
                        Width = widths[i],
                        CustomWidth = true
                    });
                }
                writer.WriteEndElement(); // </cols>

                writer.WriteStartElement(new SheetData());

                // Row 1: Title Banner
                writer.WriteStartElement(new Row { RowIndex = 1, Height = 26, CustomHeight = true });
                WriteTextCell(writer, "A1", $"{tenantName} - Payment & Expense Audit Ledger ({year})", styleIndex: 1);
                for (int col = 2; col <= 14; col++)
                {
                    WriteTextCell(writer, $"{GetColumnLetter(col)}1", string.Empty, styleIndex: 1);
                }
                writer.WriteEndElement();

                // Row 2: Subtitle (Period + Active Filters)
                var normalizedFilter = string.IsNullOrWhiteSpace(filterSummary) ? "All Records" : filterSummary;
                writer.WriteStartElement(new Row { RowIndex = 2, Height = 18, CustomHeight = true });
                WriteTextCell(
                    writer,
                    "A2",
                    $"Period: {periodLabel} | Filters: {normalizedFilter} | Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC",
                    styleIndex: 2);
                for (int col = 2; col <= 14; col++)
                {
                    WriteTextCell(writer, $"{GetColumnLetter(col)}2", string.Empty, styleIndex: 2);
                }
                writer.WriteEndElement();

                // Row 4: Column Headers
                var headers = GetHeaders();
                writer.WriteStartElement(new Row { RowIndex = 4, Height = 22, CustomHeight = true });
                for (int i = 0; i < headers.Length; i++)
                {
                    WriteTextCell(writer, $"{GetColumnLetter(i + 1)}4", headers[i], styleIndex: 3);
                }
                writer.WriteEndElement();

                // Stream DB Chunks Directly to XML
                uint currentRow = 5;
                long totalRowsWritten = 0;

                await foreach (var chunk in rowChunks.WithCancellation(ct))
                {
                    foreach (var p in chunk)
                    {
                        writer.WriteStartElement(new Row { RowIndex = currentRow });
                        var isEven = currentRow % 2 == 0;
                        uint textStyle = isEven ? 7u : 0u;
                        uint numStyle = isEven ? 8u : 4u;
                        uint centerStyle = isEven ? 9u : 5u;

                        WriteTextCell(writer, $"A{currentRow}", p.Id.ToString()[..8] + "...", textStyle);
                        WriteTextCell(writer, $"B{currentRow}", p.SubmitterName, textStyle);
                        WriteTextCell(writer, $"C{currentRow}", p.SubmitterEmail, textStyle);
                        WriteTextCell(writer, $"D{currentRow}", p.SiteName, textStyle);
                        WriteTextCell(writer, $"E{currentRow}", p.Title, textStyle);
                        WriteTextCell(writer, $"F{currentRow}", p.Description, textStyle);
                        WriteTextCell(writer, $"G{currentRow}", p.Category, textStyle);

                        WriteNumberCell(writer, $"H{currentRow}", p.RequestedAmount, numStyle);
                        if (p.ApprovedAmount.HasValue)
                        {
                            WriteNumberCell(writer, $"I{currentRow}", p.ApprovedAmount.Value, numStyle);
                        }
                        else
                        {
                            WriteTextCell(writer, $"I{currentRow}", "-", centerStyle);
                        }

                        WriteTextCell(writer, $"J{currentRow}", p.Currency, centerStyle);
                        WriteTextCell(writer, $"K{currentRow}", p.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), centerStyle);
                        WriteTextCell(writer, $"L{currentRow}", p.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), centerStyle);
                        WriteTextCell(writer, $"M{currentRow}", p.StatusName, centerStyle);
                        WriteTextCell(writer, $"N{currentRow}", p.InvoiceNumber, textStyle);

                        writer.WriteEndElement(); // </row>
                        currentRow++;
                        totalRowsWritten++;
                    }
                }

                // Totals Formula Row
                if (totalRowsWritten > 0)
                {
                    uint lastDataRow = currentRow - 1;
                    writer.WriteStartElement(new Row { RowIndex = currentRow, Height = 20, CustomHeight = true });
                    WriteTextCell(writer, $"G{currentRow}", "Totals:", styleIndex: 6);
                    WriteFormulaCell(writer, $"H{currentRow}", $"SUM(H5:H{lastDataRow})", styleIndex: 6);
                    WriteFormulaCell(writer, $"I{currentRow}", $"SUM(I5:I{lastDataRow})", styleIndex: 6);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement(); // </sheetData>

                // Merged Title & Subtitle ranges (must follow SheetData in OpenXML schema)
                writer.WriteStartElement(new MergeCells { Count = 2 });
                writer.WriteElement(new MergeCell { Reference = "A1:N1" });
                writer.WriteElement(new MergeCell { Reference = "A2:N2" });
                writer.WriteEndElement();

                writer.WriteEndElement(); // </worksheet>
                writer.Close();
                workbookPart.Workbook.Save();
            }

            return await File.ReadAllBytesAsync(tempFilePath, ct);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch
            {
                // Ignore temp cleanup race
            }
        }
    }

    private static string[] GetHeaders() =>
    [
        "Request ID",
        "Submitted By",
        "Email",
        "Site / Project",
        "Title",
        "Description",
        "Category",
        "Requested Amount (₹)",
        "Approved Amount (₹)",
        "Currency",
        "Start Date",
        "End Date",
        "Status",
        "Invoice #"
    ];

    private static string GetColumnLetter(int oneBasedCol)
        => ((char)('A' + oneBasedCol - 1)).ToString();

    private static string SanitizeXmlText(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        StringBuilder? sb = null;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = c == '\t' || c == '\n' || c == '\r' || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD);
            if (!valid)
            {
                sb ??= new StringBuilder(value.Length);
                if (sb.Length == 0 && i > 0) sb.Append(value, 0, i);
                sb.Append(' ');
            }
            else if (sb != null)
            {
                sb.Append(c);
            }
        }
        return sb?.ToString() ?? value;
    }

    private static void WriteTextCell(OpenXmlWriter writer, string cellRef, string? text, uint styleIndex)
    {
        writer.WriteStartElement(new Cell
        {
            CellReference = cellRef,
            DataType = CellValues.InlineString,
            StyleIndex = styleIndex
        });
        writer.WriteElement(new InlineString(new Text(SanitizeXmlText(text))));
        writer.WriteEndElement();
    }

    private static void WriteNumberCell(OpenXmlWriter writer, string cellRef, decimal value, uint styleIndex)
    {
        writer.WriteStartElement(new Cell
        {
            CellReference = cellRef,
            DataType = CellValues.Number,
            StyleIndex = styleIndex
        });
        writer.WriteElement(new CellValue(value.ToString("0.00", CultureInfo.InvariantCulture)));
        writer.WriteEndElement();
    }

    private static void WriteFormulaCell(OpenXmlWriter writer, string cellRef, string formula, uint styleIndex)
    {
        writer.WriteStartElement(new Cell
        {
            CellReference = cellRef,
            DataType = CellValues.Number,
            StyleIndex = styleIndex
        });
        writer.WriteElement(new CellFormula(formula));
        writer.WriteEndElement();
    }

    private static Stylesheet CreateStreamingStylesheet()
    {
        var numFormats = new NumberingFormats(
            new NumberingFormat { NumberFormatId = 164, FormatCode = "#,##0.00" }
        ) { Count = 1 };

        var fonts = new Fonts(
            new Font(new FontSize { Val = 10 }, new FontName { Val = "Calibri" }), // 0 Default
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 14 }, new FontName { Val = "Calibri" }), // 1 Title
            new Font(new Italic(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 9 }, new FontName { Val = "Calibri" }), // 2 Subtitle
            new Font(new Bold(), new Color { Rgb = "FFFFFFFF" }, new FontSize { Val = 10 }, new FontName { Val = "Calibri" }), // 3 Header
            new Font(new Bold(), new FontSize { Val = 10 }, new FontName { Val = "Calibri" }) // 4 Totals Bold
        ) { Count = 5 };

        var fills = new Fills(
            new Fill(new PatternFill { PatternType = PatternValues.None }), // 0 Required None
            new Fill(new PatternFill { PatternType = PatternValues.Gray125 }), // 1 Required Gray125
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF1E1B4B" }) { PatternType = PatternValues.Solid }), // 2 Title Indigo
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF312E81" }) { PatternType = PatternValues.Solid }), // 3 Subtitle
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FF4F46E5" }) { PatternType = PatternValues.Solid }), // 4 Header
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFF8FAFC" }) { PatternType = PatternValues.Solid }), // 5 Zebra Light
            new Fill(new PatternFill(new ForegroundColor { Rgb = "FFE0E7FF" }) { PatternType = PatternValues.Solid }) // 6 Totals Tint
        ) { Count = 7 };

        var borders = new Borders(
            new Border(), // 0 Default Empty
            new Border( // 1 Thin
                new LeftBorder { Style = BorderStyleValues.Thin },
                new RightBorder { Style = BorderStyleValues.Thin },
                new TopBorder { Style = BorderStyleValues.Thin },
                new BottomBorder { Style = BorderStyleValues.Thin })
        ) { Count = 2 };

        var cellFormats = new CellFormats(
            new CellFormat { FontId = 0, FillId = 0, BorderId = 0 }, // 0 Default Text
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center })
                { FontId = 1, FillId = 2, BorderId = 0, ApplyFont = true, ApplyFill = true, ApplyAlignment = true }, // 1 Title
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center })
                { FontId = 2, FillId = 3, BorderId = 0, ApplyFont = true, ApplyFill = true, ApplyAlignment = true }, // 2 Subtitle
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center, Vertical = VerticalAlignmentValues.Center })
                { FontId = 3, FillId = 4, BorderId = 1, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyAlignment = true }, // 3 Header
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Right })
                { FontId = 0, FillId = 0, BorderId = 0, NumberFormatId = 164, ApplyNumberFormat = true, ApplyAlignment = true }, // 4 Number
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center })
                { FontId = 0, FillId = 0, BorderId = 0, ApplyAlignment = true }, // 5 Center Text
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Right })
                { FontId = 4, FillId = 6, BorderId = 1, NumberFormatId = 164, ApplyFont = true, ApplyFill = true, ApplyBorder = true, ApplyNumberFormat = true, ApplyAlignment = true }, // 6 Totals
            new CellFormat { FontId = 0, FillId = 5, BorderId = 0, ApplyFill = true }, // 7 Zebra Text
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Right })
                { FontId = 0, FillId = 5, BorderId = 0, NumberFormatId = 164, ApplyFill = true, ApplyNumberFormat = true, ApplyAlignment = true }, // 8 Zebra Number
            new CellFormat(new Alignment { Horizontal = HorizontalAlignmentValues.Center })
                { FontId = 0, FillId = 5, BorderId = 0, ApplyFill = true, ApplyAlignment = true } // 9 Zebra Center
        ) { Count = 10 };

        return new Stylesheet(numFormats, fonts, fills, borders, cellFormats);
    }

    private static void ApplyStatusColor(IXLCell cell, string status)
    {
        switch (status)
        {
            case "Approved":
                cell.Style.Font.FontColor = XLColor.FromHtml("#166534");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dcfce7");
                break;
            case "Completed":
            case "Paid":
                cell.Style.Font.FontColor = XLColor.FromHtml("#1e40af");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
                break;
            case "Pending":
            case "PendingApproval":
                cell.Style.Font.FontColor = XLColor.FromHtml("#854d0e");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#fef9c3");
                break;
            case "InfoRequested":
                cell.Style.Font.FontColor = XLColor.FromHtml("#6b21a8");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#f3e8ff");
                break;
            case "Rejected":
                cell.Style.Font.FontColor = XLColor.FromHtml("#991b1b");
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#fee2e2");
                break;
        }
    }
}
