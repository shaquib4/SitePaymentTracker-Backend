using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public record AuditEmailNotification(
    string RecipientEmail,
    string RecipientName,
    string TenantName,
    string ReportTitle,
    string PeriodLabel,
    string FilterSummary,
    int TotalRecords,
    decimal TotalRequestedAmount,
    decimal TotalApprovedAmount,
    string PdfDownloadUrl,
    string ExcelDownloadUrl,
    string AuditFilesPortalUrl
);

public record EmailDispatchResult(bool Dispatched, string DeliveryNote);

public interface IEmailService
{
    Task<EmailDispatchResult> SendAuditReportReadyEmailAsync(
        AuditEmailNotification notification,
        CancellationToken ct = default);
}
