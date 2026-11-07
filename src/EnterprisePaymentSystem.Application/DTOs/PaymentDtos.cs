using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.DTOs;

public record CreatePaymentRequest(
    string Title,
    string Description,
    decimal Amount,
    string Currency,
    string Category,
    Guid? CategoryId,
    Guid? SiteId,
    Guid? LabourerId,
    int ExpenseYear,
    DateOnly StartDate,
    DateOnly EndDate,
    string? InvoiceNumber,
    string? ReceiptAttachmentPath
);

public record WagePaymentRequest(
    Guid LabourerId,
    Guid SiteId,
    decimal Amount,
    string Description,
    int ExpenseYear,
    DateOnly StartDate,
    DateOnly EndDate
);

public record UpdatePaymentRequest(
    string Title,
    string Description,
    decimal Amount,
    string Currency,
    string Category,
    Guid? CategoryId,
    Guid? SiteId,
    int ExpenseYear,
    DateOnly StartDate,
    DateOnly EndDate,
    string? InvoiceNumber,
    string? ReceiptAttachmentPath,
    uint Version
);

public record ApprovePaymentRequest(
    string? Comments,
    Guid? OverrideCategoryId,
    string? NewCategoryName,
    Guid? OverrideSiteId,
    uint Version
);

public record RequestInfoPaymentRequest(
    string Questions,
    uint Version
);

public record RejectPaymentRequest(
    string Reason,
    uint Version
);

public record MarkPaidRequest(
    string? Comments,
    uint Version
);

public record PaymentResponseDto(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string SubmitterName,
    string SubmitterEmail,
    string SubmitterPhone,
    string Title,
    string Description,
    decimal Amount,
    string Currency,
    string Category,
    Guid? CategoryId,
    Guid? SiteId,
    string? SiteName,
    Guid? LabourerId,
    string? LabourerName,
    int ExpenseYear,
    DateOnly StartDate,
    DateOnly EndDate,
    PaymentStatus Status,
    string StatusName,
    string? InvoiceNumber,
    string? ReceiptAttachmentPath,
    string? RejectionReason,
    string? InfoRequestDetails,
    string? ApprovalComments,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    uint Version,
    IReadOnlyList<PaymentAuditLogDto> AuditLogs
);

public record PaymentAuditLogDto(
    Guid Id,
    string Action,
    PaymentStatus? PreviousStatus,
    PaymentStatus NewStatus,
    Guid PerformedByUserId,
    string PerformedByUserName,
    DateTime TimestampUtc,
    string? Comments
);

public record PaymentFilterQuery(
    int Year,
    PaymentStatus? Status = null,
    string? Category = null,
    Guid? SiteId = null,
    Guid? RequesterId = null,
    string? Search = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null
);
