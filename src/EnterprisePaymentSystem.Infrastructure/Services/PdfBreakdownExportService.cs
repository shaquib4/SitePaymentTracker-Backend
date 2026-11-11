using System.Globalization;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class PdfBreakdownExportService : IPdfBreakdownExportService
{
    private static readonly CultureInfo IndianCulture = CultureInfo.GetCultureInfo("en-IN");

    private static readonly string[] CategoryPalette =
    [
        "#f59e0b", "#10b981", "#3b82f6", "#8b5cf6", "#ec4899",
        "#06b6d4", "#f97316", "#84cc16", "#e11d48", "#6366f1"
    ];

    public PdfBreakdownExportService()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] GenerateBreakdownPdf(PdfBreakdownContext context)
    {
        var summary = context.Summary;
        var breakdowns = summary.CategoryBreakdowns;
        var breakdownTotal = breakdowns.Sum(b => b.TotalAmount);
        var approvedPlusCompleted = summary.TotalApprovedAmount + summary.TotalCompletedAmount;
        var approvedPlusCompletedCount = summary.TotalApprovedCount + summary.TotalCompletedCount;

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(9.5f).FontColor("#1e293b"));

                page.Header().Column(header =>
                {
                    header.Item().Background("#0f172a").Padding(14).Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text(context.TenantName.ToUpperInvariant())
                                .FontSize(15).Bold().FontColor("#f59e0b");
                            col.Item().Text("EXECUTIVE EXPENSE BREAKDOWN & AUDIT SUMMARY")
                                .FontSize(10).SemiBold().FontColor("#f8fafc");
                            col.Item().Text(context.ReportTitle)
                                .FontSize(8.5f).FontColor("#94a3b8");
                        });

                        row.ConstantItem(180).AlignRight().Column(col =>
                        {
                            col.Item().AlignRight().Text("ADMIN AUDIT PACK").FontSize(8).Bold().FontColor("#fbbf24");
                            col.Item().AlignRight().Text($"Ref: #{context.JobReference}").FontSize(8).FontColor("#cbd5e1");
                            col.Item().AlignRight().Text($"Generated: {context.GeneratedAtUtc:dd MMM yyyy, HH:mm} UTC")
                                .FontSize(8).FontColor("#94a3b8");
                        });
                    });

                    header.Item().Height(6);
                });

                page.Content().Column(col =>
                {
                    col.Spacing(12);

                    // 1. Period & Active Scope Box
                    col.Item().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(10).Column(scope =>
                    {
                        scope.Spacing(4);
                        scope.Item().Text("AUDIT PERIOD & FILTER SCOPE").FontSize(8).Bold().FontColor("#475569");
                        scope.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Period: ").Bold().FontColor("#334155");
                                t.Span(context.PeriodLabel).FontColor("#0f172a");
                            });
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Site Scope: ").Bold().FontColor("#334155");
                                t.Span(string.IsNullOrWhiteSpace(context.SiteName) ? "All Sites & Head Office" : context.SiteName).FontColor("#0f172a");
                            });
                        });
                        scope.Item().Row(r =>
                        {
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Active Filters: ").Bold().FontColor("#334155");
                                t.Span(string.IsNullOrWhiteSpace(context.FilterSummary) ? "None (Full Period)" : context.FilterSummary).FontColor("#0f172a");
                            });
                            r.RelativeItem().Text(t =>
                            {
                                t.Span("Prepared By: ").Bold().FontColor("#334155");
                                t.Span($"{context.PreparedByName} ({context.PreparedByEmail})").FontColor("#0f172a");
                            });
                        });
                    });

                    // 2. Executive KPI Cards
                    col.Item().Row(kpiRow =>
                    {
                        kpiRow.Spacing(8);
                        RenderKpiCard(kpiRow.RelativeItem(), "MATCHING CLAIMS", $"{context.TotalRecords}", "Filtered Ledger Items", "#f1f5f9", "#0f172a");
                        RenderKpiCard(kpiRow.RelativeItem(), "TOTAL REQUESTED", FormatInr(context.TotalRequestedAmount), "Original Submitted Value", "#eef2ff", "#3730a3");
                        RenderKpiCard(kpiRow.RelativeItem(), "APPROVED & DISBURSED", FormatInr(approvedPlusCompleted), $"{approvedPlusCompletedCount} Cleared Claims", "#ecfdf5", "#065f46");
                        RenderKpiCard(kpiRow.RelativeItem(), "PENDING REVIEW", FormatInr(summary.TotalPendingAmount), $"{summary.TotalPendingCount} Awaiting Approval", "#fffbeb", "#92400e");
                    });

                    // 3. Category Breakdown Table (Primary Audit Schedule)
                    col.Item().Column(catSection =>
                    {
                        catSection.Spacing(5);
                        var scopeLabel = string.IsNullOrWhiteSpace(context.StatusFilter)
                            ? "Cleared (Approved & Disbursed) Category Allocation"
                            : $"{context.StatusFilter} Category Allocation";

                        catSection.Item().Row(h =>
                        {
                            h.RelativeItem().Text("EXPENSE CATEGORY BREAKDOWN").FontSize(10.5f).Bold().FontColor("#0f172a");
                            h.AutoItem().Text(scopeLabel).FontSize(8.5f).SemiBold().FontColor("#64748b");
                        });

                        if (breakdowns.Count == 0)
                        {
                            catSection.Item().Border(1).BorderColor("#e2e8f0").Padding(14).AlignCenter()
                                .Text("No expense categories matched the selected period and filter criteria.")
                                .FontSize(9).Italic().FontColor("#64748b");
                        }
                        else
                        {
                            catSection.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.ConstantColumn(28);
                                    columns.RelativeColumn(3.2f);
                                    columns.ConstantColumn(48);
                                    columns.ConstantColumn(52);
                                    columns.RelativeColumn(2.2f);
                                    columns.ConstantColumn(105);
                                });

                                table.Header(header =>
                                {
                                    AddHeaderCell(header.Cell(), "#");
                                    AddHeaderCell(header.Cell(), "Category");
                                    AddHeaderCell(header.Cell(), "Claims", right: true);
                                    AddHeaderCell(header.Cell(), "Share %", right: true);
                                    AddHeaderCell(header.Cell(), "Distribution");
                                    AddHeaderCell(header.Cell(), "Amount (INR)", right: true);
                                });

                                for (int i = 0; i < breakdowns.Count; i++)
                                {
                                    var item = breakdowns[i];
                                    var pct = breakdownTotal > 0m ? (double)(item.TotalAmount / breakdownTotal * 100m) : 0d;
                                    var color = CategoryPalette[i % CategoryPalette.Length];
                                    var bg = i % 2 == 0 ? "#ffffff" : "#f8fafc";

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6)
                                        .Text($"{i + 1}").FontColor("#64748b");

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6).Row(r =>
                                    {
                                        r.AutoItem().PaddingTop(3).Width(8).Height(8).Background(color);
                                        r.RelativeItem().PaddingLeft(6).Text(item.Category).SemiBold().FontColor("#0f172a");
                                    });

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6)
                                        .AlignRight().Text($"{item.Count}").FontColor("#334155");

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6)
                                        .AlignRight().Text($"{pct:F1}%").SemiBold().FontColor("#334155");

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6)
                                        .AlignMiddle().Column(barCol =>
                                        {
                                            var fillPct = Math.Clamp((float)pct, 0f, 100f);
                                            barCol.Item().Height(7).Background("#e2e8f0").Row(bar =>
                                            {
                                                if (fillPct > 0.5f)
                                                {
                                                    bar.RelativeItem(fillPct).Background(color);
                                                }
                                                if (fillPct < 99.5f)
                                                {
                                                    bar.RelativeItem(100f - fillPct);
                                                }
                                            });
                                        });

                                    table.Cell().Background(bg).BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(6)
                                        .AlignRight().Text(FormatInr(item.TotalAmount)).Bold().FontColor("#0f172a");
                                }

                                // Footer Total Row
                                table.Cell().Background("#e2e8f0").Padding(6).Text("");
                                table.Cell().Background("#e2e8f0").Padding(6).Text("TOTAL BREAKDOWN").Bold().FontColor("#0f172a");
                                table.Cell().Background("#e2e8f0").Padding(6).AlignRight().Text($"{breakdowns.Sum(b => b.Count)}").Bold().FontColor("#0f172a");
                                table.Cell().Background("#e2e8f0").Padding(6).AlignRight().Text("100.0%").Bold().FontColor("#0f172a");
                                table.Cell().Background("#e2e8f0").Padding(6).Text("");
                                table.Cell().Background("#e2e8f0").Padding(6).AlignRight().Text(FormatInr(breakdownTotal)).Bold().FontColor("#0f172a");
                            });
                        }
                    });

                    // 4. Status Summary Table
                    col.Item().Column(statusSection =>
                    {
                        statusSection.Spacing(5);
                        statusSection.Item().Text("STATUS SUMMARY ACROSS MATCHING RECORDS").FontSize(10).Bold().FontColor("#0f172a");

                        var statusRows = new[]
                        {
                            ("Completed / Disbursed", summary.TotalCompletedCount, summary.TotalCompletedAmount, "#1e40af"),
                            ("Approved (Awaiting Settlement)", summary.TotalApprovedCount, summary.TotalApprovedAmount, "#065f46"),
                            ("Pending Approval", summary.TotalPendingCount, summary.TotalPendingAmount, "#92400e"),
                            ("Clarification Requested", summary.TotalInfoRequestedCount, summary.TotalInfoRequestedAmount, "#6b21a8"),
                            ("Rejected", summary.TotalRejectedCount, summary.TotalRejectedAmount, "#991b1b")
                        };

                        statusSection.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(70);
                                columns.ConstantColumn(120);
                            });

                            table.Header(header =>
                            {
                                AddHeaderCell(header.Cell(), "Approval Workflow Status");
                                AddHeaderCell(header.Cell(), "Records", right: true);
                                AddHeaderCell(header.Cell(), "Share %", right: true);
                                AddHeaderCell(header.Cell(), "Total Value (INR)", right: true);
                            });

                            foreach (var (label, count, amount, colorHex) in statusRows)
                            {
                                var share = summary.GrandTotalAmount > 0m ? (double)(amount / summary.GrandTotalAmount * 100m) : 0d;
                                table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(5)
                                    .Text(label).SemiBold().FontColor(colorHex);
                                table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(5)
                                    .AlignRight().Text($"{count}");
                                table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(5)
                                    .AlignRight().Text($"{share:F1}%");
                                table.Cell().BorderBottom(0.5f).BorderColor("#e2e8f0").Padding(5)
                                    .AlignRight().Text(FormatInr(amount)).Bold();
                            }

                            table.Cell().Background("#f1f5f9").Padding(6).Text("GRAND TOTAL (EFFECTIVE VALUE)").Bold().FontColor("#0f172a");
                            table.Cell().Background("#f1f5f9").Padding(6).AlignRight().Text($"{context.TotalRecords}").Bold();
                            table.Cell().Background("#f1f5f9").Padding(6).AlignRight().Text("100.0%").Bold();
                            table.Cell().Background("#f1f5f9").Padding(6).AlignRight().Text(FormatInr(summary.GrandTotalAmount)).Bold();
                        });
                    });

                    // 5. Companion Excel Note & Auditor Sign-Off
                    col.Item().Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(10).Column(note =>
                    {
                        note.Spacing(4);
                        note.Item().Text("COMPANION ITEMIZED LEDGER REFERENCE").FontSize(8).Bold().FontColor("#334155");
                        note.Item().Text(
                            $"This PDF contains executive category & status breakdowns only. All {context.TotalRecords} itemized expense transactions matching this period and filter criteria are packaged in the companion Excel ledger: {context.ExcelFileName}.")
                            .FontSize(8.5f).FontColor("#475569");
                    });

                    col.Item().PaddingTop(14).Row(signRow =>
                    {
                        signRow.Spacing(24);
                        RenderSignatureBox(signRow.RelativeItem(), "Prepared By (Admin)", context.PreparedByName);
                        RenderSignatureBox(signRow.RelativeItem(), "Reviewed By (Audit / Accounts)", "Authorized Signatory");
                        RenderSignatureBox(signRow.RelativeItem(), "Verification Date & Seal", DateTime.UtcNow.ToString("dd MMM yyyy"));
                    });
                });

                page.Footer().PaddingTop(8).BorderTop(0.5f).BorderColor("#cbd5e1").Row(footer =>
                {
                    footer.RelativeItem().Text($"{context.TenantName} • Confidential Admin Audit Document")
                        .FontSize(8).FontColor("#64748b");
                    footer.AutoItem().Text(x =>
                    {
                        x.DefaultTextStyle(s => s.FontSize(8).FontColor("#64748b"));
                        x.Span("Page ");
                        x.CurrentPageNumber();
                        x.Span(" of ");
                        x.TotalPages();
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    private static string FormatInr(decimal amount)
        => $"INR {amount.ToString("N2", IndianCulture)}";

    private static void RenderKpiCard(
        IContainer container,
        string label,
        string value,
        string subtext,
        string bgHex,
        string accentHex)
    {
        container.Border(1).BorderColor("#e2e8f0").Background(bgHex).Padding(9).Column(col =>
        {
            col.Spacing(2);
            col.Item().Text(label).FontSize(7.5f).Bold().FontColor("#475569");
            col.Item().Text(value).FontSize(11).Bold().FontColor(accentHex);
            col.Item().Text(subtext).FontSize(7.5f).FontColor("#64748b");
        });
    }

    private static void AddHeaderCell(IContainer cell, string text, bool right = false)
    {
        var styled = cell.Background("#1e293b").Padding(6);
        if (right)
        {
            styled.AlignRight().Text(text).FontSize(8.5f).Bold().FontColor(Colors.White);
        }
        else
        {
            styled.Text(text).FontSize(8.5f).Bold().FontColor(Colors.White);
        }
    }

    private static void RenderSignatureBox(IContainer container, string caption, string subline)
    {
        container.Column(col =>
        {
            col.Item().Height(22);
            col.Item().BorderTop(1).BorderColor("#94a3b8").PaddingTop(4).Text(caption).FontSize(8).Bold().FontColor("#334155");
            col.Item().Text(subline).FontSize(7.5f).FontColor("#64748b");
        });
    }
}
