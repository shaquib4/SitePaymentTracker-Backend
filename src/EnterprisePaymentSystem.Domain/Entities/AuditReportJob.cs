using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Domain.Entities;

public class AuditReportJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }

    public Guid RequestedByUserId { get; set; }
    public string RequestedByName { get; set; } = string.Empty;
    public string RequestedByEmail { get; set; } = string.Empty;

    public string ReportTitle { get; set; } = string.Empty;
    public string PeriodLabel { get; set; } = string.Empty;
    public string FilterSummary { get; set; } = string.Empty;

    public int Year { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? StatusFilter { get; set; }
    public string? CategoryFilter { get; set; }
    public Guid? SiteId { get; set; }
    public string? SiteName { get; set; }
    public string? SearchFilter { get; set; }

    public AuditReportStatus Status { get; set; } = AuditReportStatus.Queued;
    public int TotalRecords { get; set; }
    public decimal TotalRequestedAmount { get; set; }
    public decimal TotalApprovedAmount { get; set; }

    public string PdfFileName { get; set; } = string.Empty;
    public string ExcelFileName { get; set; } = string.Empty;
    public byte[]? PdfBytes { get; set; }
    public byte[]? ExcelBytes { get; set; }

    public string DownloadToken { get; set; } = string.Empty;
    public bool EmailDispatched { get; set; }
    public string? EmailDeliveryNote { get; set; }

    public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }
}
