namespace EnterprisePaymentSystem.Application.DTOs;

public record FinanceMetricsDto(
    int TotalRequests,
    int PendingApprovalCount,
    int ApprovedCount,
    int PaidCount,
    int RejectedCount,
    decimal TotalPendingAmount,
    decimal TotalApprovedAmount,
    decimal TotalPaidAmount,
    string Currency,
    IReadOnlyList<CategoryBreakdownDto> CategoryBreakdowns,
    IReadOnlyList<MonthlyExpenseDto> MonthlyTrend
);

public record CategoryBreakdownDto(
    string Category,
    int Count,
    decimal TotalAmount
);

public record MonthlyExpenseDto(
    int Month,
    string MonthName,
    decimal TotalAmount,
    int Count
);
