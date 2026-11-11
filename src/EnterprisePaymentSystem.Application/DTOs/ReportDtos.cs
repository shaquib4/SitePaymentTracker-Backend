using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.DTOs;

public enum ExportJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public record AuditExportFilter(
    int Year,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    PaymentStatus? Status = null,
    string? Category = null,
    Guid? SiteId = null,
    string? Search = null,
    Guid? EmployeeId = null
);

public record AdminAuditPackRequest(
    int Year,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    PaymentStatus? Status = null,
    string? Category = null,
    Guid? SiteId = null,
    string? Search = null,
    string? RecipientEmail = null,
    string? ReportTitle = null,
    Guid? EmployeeId = null
);

public record ExportJobRequest(
    string JobId,
    Guid TenantId,
    string TenantName,
    int Year,
    Guid RequestedByUserId,
    string RequestedByEmail,
    DateTime QueuedAtUtc,
    bool IsAuditPack = false,
    Guid? AuditReportJobId = null,
    AuditExportFilter? AuditFilter = null,
    string? RequestedByName = null,
    string? BaseApiUrl = null,
    string? FrontendUrl = null
);

public record ExportReportRequest(
    int Year
);

public record ExportJobStatusResponse(
    string JobId,
    ExportJobStatus Status,
    string StatusName,
    int Year,
    DateTime QueuedAtUtc,
    string? DownloadUrl,
    string? ErrorMessage
);

public record RegenerateAuditPackRequest(
    string? RecipientEmail = null
);

public record EmailConfigurationStatusDto(
    bool IsConfigured,
    string Provider,
    string? FromAddress,
    string Message
);

public record AuditExpenseRowDto(
    Guid Id,
    string SubmitterName,
    string SubmitterEmail,
    string SiteName,
    string Title,
    string Description,
    string Category,
    decimal RequestedAmount,
    decimal? ApprovedAmount,
    string Currency,
    DateOnly StartDate,
    DateOnly EndDate,
    string StatusName,
    string InvoiceNumber
);

public record AuditReportJobDto(
    Guid Id,
    string ReportTitle,
    string PeriodLabel,
    string FilterSummary,
    int Year,
    DateOnly? StartDate,
    DateOnly? EndDate,
    string? StatusFilter,
    string? CategoryFilter,
    Guid? SiteId,
    string? SiteName,
    string? SearchFilter,
    ExportJobStatus Status,
    string StatusName,
    int TotalRecords,
    decimal TotalRequestedAmount,
    decimal TotalApprovedAmount,
    string PdfFileName,
    string ExcelFileName,
    bool HasPdf,
    bool HasExcel,
    DateTime? PdfDownloadedAtUtc,
    DateTime? ExcelDownloadedAtUtc,
    string RequestedByName,
    string RequestedByEmail,
    bool EmailDispatched,
    string? EmailDeliveryNote,
    DateTime QueuedAtUtc,
    DateTime? CompletedAtUtc,
    string? ErrorMessage,
    Guid? EmployeeId = null,
    string? EmployeeName = null
);
