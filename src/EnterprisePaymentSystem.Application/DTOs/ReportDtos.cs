namespace EnterprisePaymentSystem.Application.DTOs;

public enum ExportJobStatus
{
    Queued = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public record ExportJobRequest(
    string JobId,
    Guid TenantId,
    string TenantName,
    int Year,
    Guid RequestedByUserId,
    string RequestedByEmail,
    DateTime QueuedAtUtc
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
