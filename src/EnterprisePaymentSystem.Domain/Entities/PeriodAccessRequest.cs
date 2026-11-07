namespace EnterprisePaymentSystem.Domain.Entities;

public class PeriodAccessRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid RequestedByUserId { get; set; }
    public User? RequestedByUser { get; set; }

    public int TargetYear { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string Status { get; set; } = "Pending"; // "Pending", "Approved", "Rejected"
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public Guid? ReviewedByUserId { get; set; }
    public User? ReviewedByUser { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? AdminNotes { get; set; }
}

