namespace EnterprisePaymentSystem.Application.DTOs;

public record LabourerDto(
    Guid Id,
    string Name,
    decimal DailyWageRate,
    string? PhotoUrl,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid SupervisorId,
    string SupervisorName,
    string SupervisorPhone,
    string SupervisorSixDigitId,
    Guid? SiteId,
    string? SiteName,
    Guid? CreatedByUserId,
    string CreatedByUserName,
    string CreatedByUserRole,
    decimal TotalWagesClaimed,
    decimal TotalWagesApproved,
    int TotalWageClaimsCount,
    int PendingWageClaimsCount,
    int ApprovedWageClaimsCount,
    int TotalDaysWorked
);

public record CreateLabourerDto(
    string Name,
    decimal DailyWageRate,
    string? PhotoUrl = null,
    Guid? SupervisorId = null,
    Guid? SiteId = null
);

public record UpdateLabourerDto(
    string Name,
    decimal DailyWageRate,
    Guid? SiteId = null,
    Guid? SupervisorId = null,
    string? PhotoUrl = null,
    bool? IsActive = null
);

public record LabourerWagePayoutItemDto(
    Guid PaymentId,
    string Title,
    string? Description,
    Guid? SiteId,
    string? SiteName,
    DateOnly StartDate,
    DateOnly EndDate,
    int ClaimDays,
    decimal DailyWageRate,
    decimal ExpectedWage,
    decimal ActualAmount,
    decimal Variance,
    bool HasDiscrepancy,
    string Status,
    DateTime RequestDate,
    string RequestedBySupervisorName,
    string RequestedBySupervisorPhone,
    DateTime? ApprovalDate,
    string? ApprovedByUserName,
    string? ApprovalComments,
    string? RejectionReason
);

public record LabourerWageAuditResponse(
    Guid LabourerId,
    string Name,
    decimal DailyWageRate,
    string? PhotoUrl,
    bool IsActive,
    DateTime CreatedAtUtc,
    Guid SupervisorId,
    string SupervisorName,
    string SupervisorPhone,
    string SupervisorSixDigitId,
    Guid? CreatedByUserId,
    string CreatedByUserName,
    string CreatedByUserRole,
    decimal TotalApprovedAmount,
    decimal TotalPendingAmount,
    decimal TotalPaidAmount,
    decimal TotalClaimedAmount,
    int TotalDaysClaimed,
    decimal ExpectedTotalWages,
    decimal TotalVariance,
    string ExploitationRisk,
    string ExploitationRiskDescription,
    List<LabourerWagePayoutItemDto> Payouts
);
