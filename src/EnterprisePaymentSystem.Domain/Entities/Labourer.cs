namespace EnterprisePaymentSystem.Domain.Entities;

public class Labourer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid SupervisorId { get; set; }
    public User? Supervisor { get; set; }

    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }

    public Guid? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? PhotoUrl { get; set; }
    public decimal DailyWageRate { get; set; }
    public bool IsActive { get; set; } = true;
    public bool IsDeleted { get; set; } = false;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PaymentRequest> WagePayments { get; set; } = new List<PaymentRequest>();
}

