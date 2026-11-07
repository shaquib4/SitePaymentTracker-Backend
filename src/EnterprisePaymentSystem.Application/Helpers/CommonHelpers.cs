using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.Helpers;

public class DateRangeHelper
{
    public bool IsStrictlyWithinYear(DateOnly date, int expectedYear)
    {
        return date.Year == expectedYear;
    }

    public bool IsValidRange(DateOnly startDate, DateOnly endDate, int expectedYear)
    {
        return startDate.Year == expectedYear &&
               endDate.Year == expectedYear &&
               endDate >= startDate;
    }
}

public class AuditTrailHelper
{
    public PaymentAuditLog CreateLogEntry(
        Guid tenantId,
        Guid paymentRequestId,
        string action,
        PaymentStatus? previousStatus,
        PaymentStatus newStatus,
        Guid userId,
        string userName,
        string? comments = null)
    {
        return new PaymentAuditLog
        {
            TenantId = tenantId,
            PaymentRequestId = paymentRequestId,
            Action = action,
            PreviousStatus = previousStatus,
            NewStatus = newStatus,
            PerformedByUserId = userId,
            PerformedByUserName = string.IsNullOrWhiteSpace(userName) ? "System User" : userName,
            TimestampUtc = DateTime.UtcNow,
            Comments = comments
        };
    }
}

public class CurrencyHelper
{
    public string Format(decimal amount, string currency = "INR")
    {
        var symbol = currency.ToUpperInvariant() switch
        {
            "INR" => "₹",
            "USD" => "$",
            "EUR" => "€",
            "GBP" => "£",
            _ => currency + " "
        };

        return $"{symbol} {amount:N2}";
    }
}
