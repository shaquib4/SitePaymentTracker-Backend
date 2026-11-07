namespace EnterprisePaymentSystem.Domain.Entities;

public class Site
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string Name { get; set; } = string.Empty; // e.g. "Metro Line Project", "Tower 3 Construction"
    public string Location { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public decimal? ContractValue { get; set; }
    public decimal? FinalPaymentReceived { get; set; }
    public bool IsSettled { get; set; } = false;
    public DateTime? SettledAtUtc { get; set; }

    public ICollection<PaymentRequest> PaymentRequests { get; set; } = new List<PaymentRequest>();
    public ICollection<MeasurementBook> MeasurementBooks { get; set; } = new List<MeasurementBook>();
}

