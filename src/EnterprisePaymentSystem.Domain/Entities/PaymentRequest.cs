using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Domain.Exceptions;

namespace EnterprisePaymentSystem.Domain.Entities;

public class PaymentRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    // Requester (Supervisor / Employee)
    public Guid UserId { get; set; }
    public User? User { get; set; }

    // Dynamic Category relation
    public Guid? CategoryId { get; set; }
    public ExpenseCategory? CategoryEntity { get; set; }
    public string Category { get; set; } = "General";

    // Labour Wage Link (Nullable - separates material from labour wage expenses)
    public Guid? LabourerId { get; set; }
    public Labourer? Labourer { get; set; }

    // Project Site Link (Nullable - connects expense to a specific site for profitability calculation)
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Strict Financial Precision: NUMERIC(14,2)
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "INR";

    // Strictly bounded single-year fields
    public int ExpenseYear { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public DateTime RequestDate { get; set; } = DateTime.UtcNow;

    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public string? InvoiceNumber { get; set; }
    public string? ReceiptAttachmentPath { get; set; }
    public string? RejectionReason { get; set; }
    public string? InfoRequestDetails { get; set; }
    public string? ApprovalComments { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }

    // Optimistic Concurrency Token
    public uint Version { get; set; } = 1;

    public ICollection<PaymentAuditLog> AuditLogs { get; set; } = new List<PaymentAuditLog>();

    // State Machine Transitions
    public void Submit(Guid userId, string userName, string? comments = null)
    {
        var prev = Status;
        Status = PaymentStatus.Pending;
        UpdatedAtUtc = DateTime.UtcNow;
        RejectionReason = null;

        AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = TenantId,
            PaymentRequestId = Id,
            Action = "SubmittedForApproval",
            PreviousStatus = prev,
            NewStatus = Status,
            PerformedByUserId = userId,
            PerformedByUserName = userName,
            TimestampUtc = DateTime.UtcNow,
            Comments = comments ?? "Submitted for approval"
        });
    }

    public void RequestInfo(Guid reviewerUserId, string reviewerUserName, string questions)
    {
        if (string.IsNullOrWhiteSpace(questions))
        {
            throw new DomainException("Questions/details must be specified when requesting more information.");
        }

        if (Status != PaymentStatus.Pending)
        {
            throw new DomainException($"Cannot request more info on payment in status '{Status}'. Only Pending requests can be flagged.");
        }

        var prev = Status;
        Status = PaymentStatus.InfoRequested;
        InfoRequestDetails = questions;
        UpdatedAtUtc = DateTime.UtcNow;

        AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = TenantId,
            PaymentRequestId = Id,
            Action = "InfoRequested",
            PreviousStatus = prev,
            NewStatus = Status,
            PerformedByUserId = reviewerUserId,
            PerformedByUserName = reviewerUserName,
            TimestampUtc = DateTime.UtcNow,
            Comments = $"{reviewerUserName} requested details: {questions}"
        });
    }

    public void Approve(
        Guid adminUserId,
        string adminUserName,
        string? comments = null,
        Guid? overrideCategoryId = null,
        string? overrideCategoryName = null,
        Guid? overrideSiteId = null)
    {
        if (Status != PaymentStatus.Pending && Status != PaymentStatus.InfoRequested)
        {
            throw new DomainException($"Cannot approve payment in status '{Status}'. Only Pending or InfoRequested requests can be approved.");
        }

        var prev = Status;
        Status = PaymentStatus.Approved;
        ApprovalComments = comments;
        UpdatedAtUtc = DateTime.UtcNow;

        // Admin can dynamically re-categorize or link site during approval
        if (overrideCategoryId.HasValue)
        {
            CategoryId = overrideCategoryId.Value;
        }
        if (!string.IsNullOrWhiteSpace(overrideCategoryName))
        {
            Category = overrideCategoryName.Trim();
        }
        if (overrideSiteId.HasValue)
        {
            SiteId = overrideSiteId.Value;
        }

        AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = TenantId,
            PaymentRequestId = Id,
            Action = "Approved",
            PreviousStatus = prev,
            NewStatus = Status,
            PerformedByUserId = adminUserId,
            PerformedByUserName = adminUserName,
            TimestampUtc = DateTime.UtcNow,
            Comments = comments ?? "Approved by administrator"
        });
    }

    public void Reject(Guid adminUserId, string adminUserName, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("A rejection reason must be provided.");
        }

        var prev = Status;
        Status = PaymentStatus.Rejected;
        RejectionReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;

        AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = TenantId,
            PaymentRequestId = Id,
            Action = "Rejected",
            PreviousStatus = prev,
            NewStatus = Status,
            PerformedByUserId = adminUserId,
            PerformedByUserName = adminUserName,
            TimestampUtc = DateTime.UtcNow,
            Comments = reason
        });
    }

    public void MarkCompleted(Guid adminUserId, string adminUserName, string? comments = null)
    {
        if (Status != PaymentStatus.Approved)
        {
            throw new DomainException($"Cannot mark payment as Completed in status '{Status}'. Only Approved requests can be completed.");
        }

        var prev = Status;
        Status = PaymentStatus.Completed;
        UpdatedAtUtc = DateTime.UtcNow;

        AuditLogs.Add(new PaymentAuditLog
        {
            TenantId = TenantId,
            PaymentRequestId = Id,
            Action = "CompletedAndDisbursed",
            PreviousStatus = prev,
            NewStatus = Status,
            PerformedByUserId = adminUserId,
            PerformedByUserName = adminUserName,
            TimestampUtc = DateTime.UtcNow,
            Comments = comments ?? "Payment finalized and disbursed"
        });
    }
}
