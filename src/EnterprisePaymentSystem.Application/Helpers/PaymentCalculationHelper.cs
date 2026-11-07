using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.Helpers;

public class PaymentCalculationHelper
{
    public FinanceMetricsDto CalculateMetrics(IReadOnlyList<PaymentRequest> payments, int year, string defaultCurrency = "INR")
    {
        var pending = payments.Where(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.InfoRequested).ToList();
        var approved = payments.Where(p => p.Status == PaymentStatus.Approved).ToList();
        var completed = payments.Where(p => p.Status == PaymentStatus.Completed).ToList();
        var rejected = payments.Where(p => p.Status == PaymentStatus.Rejected).ToList();

        var approvedPayments = payments
            .Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed)
            .ToList();

        // Pie Chart strictly aggregates only Approved & Completed expenditures (Pending and Rejected are excluded)
        var categories = approvedPayments
            .GroupBy(p => p.Category)
            .Select(g => new CategoryBreakdownDto(g.Key, g.Count(), g.Sum(x => x.Amount)))
            .OrderByDescending(c => c.TotalAmount)
            .ToList();

        var monthly = Enumerable.Range(1, 12).Select(month =>
        {
            var inMonth = approvedPayments.Where(p => p.StartDate.Month == month).ToList();
            var monthName = new DateTime(year, month, 1).ToString("MMM");
            return new MonthlyExpenseDto(month, monthName, inMonth.Sum(x => x.Amount), inMonth.Count);
        }).ToList();

        return new FinanceMetricsDto(
            TotalRequests: payments.Count,
            PendingApprovalCount: pending.Count,
            ApprovedCount: approved.Count,
            PaidCount: completed.Count,
            RejectedCount: rejected.Count,
            TotalPendingAmount: pending.Sum(x => x.Amount),
            TotalApprovedAmount: approved.Sum(x => x.Amount),
            TotalPaidAmount: completed.Sum(x => x.Amount),
            Currency: payments.FirstOrDefault()?.Currency ?? defaultCurrency,
            CategoryBreakdowns: categories,
            MonthlyTrend: monthly
        );
    }
}
