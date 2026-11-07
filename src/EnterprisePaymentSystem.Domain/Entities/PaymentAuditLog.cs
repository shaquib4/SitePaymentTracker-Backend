using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Domain.Entities;

public class PaymentAuditLog
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid PaymentRequestId { get; set; }
    public PaymentRequest? PaymentRequest { get; set; }

    public string Action { get; set; } = string.Empty;
    public PaymentStatus? PreviousStatus { get; set; }
    public PaymentStatus NewStatus { get; set; }
    public Guid PerformedByUserId { get; set; }
    public string PerformedByUserName { get; set; } = string.Empty;
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public string? Comments { get; set; }
}
