using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public record PdfBreakdownContext(
    string JobReference,
    string TenantName,
    string ReportTitle,
    string PeriodLabel,
    string FilterSummary,
    string PreparedByName,
    string PreparedByEmail,
    string? SiteName,
    string? StatusFilter,
    string? CategoryFilter,
    string ExcelFileName,
    int TotalRecords,
    decimal TotalRequestedAmount,
    decimal TotalApprovedAmount,
    PaymentPagedSummaryDto Summary,
    DateTime GeneratedAtUtc
);

public interface IPdfBreakdownExportService
{
    byte[] GenerateBreakdownPdf(PdfBreakdownContext context);
}
