using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Domain.Exceptions;
using FluentAssertions;

namespace EnterprisePaymentSystem.UnitTests;

public class HelperAndDomainTests
{
    private readonly DateRangeHelper _dateHelper = new();
    private readonly PaymentCalculationHelper _calcHelper = new();
    private readonly AuditTrailHelper _auditHelper = new();

    [Fact]
    public void DateRangeHelper_ShouldAcceptDatesWithinSameExpenseYear()
    {
        var start = new DateOnly(2026, 1, 15);
        var end = new DateOnly(2026, 6, 30);

        var isValid = _dateHelper.IsValidRange(start, end, 2026);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void DateRangeHelper_ShouldRejectDatesSpanningDifferentYears()
    {
        var start = new DateOnly(2025, 12, 1);
        var end = new DateOnly(2026, 1, 15);

        var isValid = _dateHelper.IsValidRange(start, end, 2026);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void DateRangeHelper_ShouldRejectEndDateBeforeStartDate()
    {
        var start = new DateOnly(2026, 5, 15);
        var end = new DateOnly(2026, 5, 10);

        var isValid = _dateHelper.IsValidRange(start, end, 2026);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void AuditTrailHelper_ShouldCreateStandardAuditLog()
    {
        var tenantId = Guid.NewGuid();
        var paymentId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var log = _auditHelper.CreateLogEntry(
            tenantId,
            paymentId,
            "Approved",
            PaymentStatus.Pending,
            PaymentStatus.Approved,
            userId,
            "Admin User",
            "Approved budget"
        );

        log.TenantId.Should().Be(tenantId);
        log.PaymentRequestId.Should().Be(paymentId);
        log.Action.Should().Be("Approved");
        log.PreviousStatus.Should().Be(PaymentStatus.Pending);
        log.NewStatus.Should().Be(PaymentStatus.Approved);
        log.PerformedByUserId.Should().Be(userId);
        log.PerformedByUserName.Should().Be("Admin User");
        log.Comments.Should().Be("Approved budget");
    }

    [Fact]
    public void PaymentCalculationHelper_ShouldCalculateCorrectTotalsAndBreakdowns()
    {
        var payments = new List<PaymentRequest>
        {
            new()
            {
                Amount = 1000m,
                Category = "Cement",
                Status = PaymentStatus.Pending,
                StartDate = new DateOnly(2026, 1, 10)
            },
            new()
            {
                Amount = 2500m,
                Category = "Sariya",
                Status = PaymentStatus.Approved,
                StartDate = new DateOnly(2026, 1, 20)
            },
            new()
            {
                Amount = 5000m,
                Category = "Labour Wages",
                Status = PaymentStatus.Completed,
                StartDate = new DateOnly(2026, 2, 5)
            }
        };

        var metrics = _calcHelper.CalculateMetrics(payments, 2026, "INR");

        metrics.TotalRequests.Should().Be(3);
        metrics.PendingApprovalCount.Should().Be(1);
        metrics.TotalPendingAmount.Should().Be(1000m);
        metrics.ApprovedCount.Should().Be(1);
        metrics.TotalApprovedAmount.Should().Be(2500m);
        metrics.PaidCount.Should().Be(1);
        metrics.TotalPaidAmount.Should().Be(5000m);
        // CategoryBreakdowns strictly excludes Pending/Rejected payments for Pie Chart integrity
        metrics.CategoryBreakdowns.Should().HaveCount(2);
        metrics.CategoryBreakdowns.Should().NotContain(c => c.Category == "Cement");
    }

    [Fact]
    public void PaymentRequest_StateTransitions_ShouldFollowStrictRules()
    {
        var payment = new PaymentRequest
        {
            TenantId = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Status = PaymentStatus.Pending
        };

        var adminId = Guid.NewGuid();

        // 1. Approve
        payment.Approve(adminId, "Admin", "All clear");
        payment.Status.Should().Be(PaymentStatus.Approved);
        payment.ApprovalComments.Should().Be("All clear");

        // 2. Mark Completed
        payment.MarkCompleted(adminId, "Admin", "Disbursed via bank transfer");
        payment.Status.Should().Be(PaymentStatus.Completed);

        // 3. Invalid Transition: Cannot Approve an already Completed request
        var act = () => payment.Approve(adminId, "Admin");
        act.Should().Throw<DomainException>()
            .WithMessage("*Cannot approve payment in status 'Completed'*");
    }

    [Fact]
    public void PaymentRequest_AccountantCanRequestMoreInfo()
    {
        var payment = new PaymentRequest
        {
            Status = PaymentStatus.Pending
        };

        var accountantId = Guid.NewGuid();
        payment.RequestInfo(accountantId, "Pooja Verma", "Missing weighing slip for Sariya");

        payment.Status.Should().Be(PaymentStatus.InfoRequested);
        payment.InfoRequestDetails.Should().Be("Missing weighing slip for Sariya");
        payment.AuditLogs.Should().ContainSingle(a => a.Action == "InfoRequested");
    }

    [Fact]
    public void PaymentRequest_AdminCanAlsoRequestMoreInfo()
    {
        var payment = new PaymentRequest
        {
            Status = PaymentStatus.Pending
        };

        var adminId = Guid.NewGuid();
        payment.RequestInfo(adminId, "Rajesh Sharma", "Admin requires delivery verification");

        payment.Status.Should().Be(PaymentStatus.InfoRequested);
        payment.InfoRequestDetails.Should().Be("Admin requires delivery verification");
        payment.AuditLogs.Should().ContainSingle(a => a.Action == "InfoRequested" && a.PerformedByUserName == "Rajesh Sharma");
    }
}
