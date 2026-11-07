using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Domain.Entities;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TenantId { get; set; }
    public Tenant? Tenant { get; set; }

    public string? Email { get; set; }
    public string FullName { get; set; } = string.Empty;

    // Mandatory Phone Number for Admin to click-to-call and verify expenses
    public string PhoneNumber { get; set; } = string.Empty;

    // Generated 6-digit User ID (e.g., "592314")
    public string SixDigitUserId { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Supervisor;
    public string Department { get; set; } = "Operations";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public ICollection<PaymentRequest> PaymentRequests { get; set; } = new List<PaymentRequest>();
    public ICollection<Labourer> ManagedLabourers { get; set; } = new List<Labourer>();
}
