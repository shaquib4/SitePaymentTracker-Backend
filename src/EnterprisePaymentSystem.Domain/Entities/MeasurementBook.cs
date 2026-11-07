namespace EnterprisePaymentSystem.Domain.Entities;

public class MeasurementBook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public Guid SiteId { get; set; }
    public Site? Site { get; set; }

    public int Month { get; set; }
    public int Year { get; set; }
    public string? ExcelFileUrl { get; set; }
    public decimal TotalEstimatedValue { get; set; } // Estimated value from MB Excel
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}

