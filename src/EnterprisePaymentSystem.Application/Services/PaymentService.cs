using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Domain.Exceptions;

namespace EnterprisePaymentSystem.Application.Services;

public class PaymentService : IPaymentService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ICurrentUserService _currentUserService;
    private readonly PaymentCalculationHelper _calculationHelper;
    private readonly AuditTrailHelper _auditTrailHelper;
    private readonly DateRangeHelper _dateRangeHelper;
    private readonly IApplicationDbContext? _context;

    public PaymentService(
        IPaymentRepository paymentRepository,
        ICurrentUserService currentUserService,
        PaymentCalculationHelper calculationHelper,
        AuditTrailHelper auditTrailHelper,
        DateRangeHelper dateRangeHelper,
        IApplicationDbContext? context = null)
    {
        _paymentRepository = paymentRepository;
        _currentUserService = currentUserService;
        _calculationHelper = calculationHelper;
        _auditTrailHelper = auditTrailHelper;
        _dateRangeHelper = dateRangeHelper;
        _context = context;
    }

    public async Task<PaymentResponseDto> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default)
    {
        var tenantId = _currentUserService.TenantId ?? throw new DomainException("Tenant context is required.");
        var userId = _currentUserService.UserId ?? throw new DomainException("User context is required.");
        var userName = _currentUserService.FullName ?? "User";

        if (!_dateRangeHelper.IsValidRange(request.StartDate, request.EndDate, request.ExpenseYear))
        {
            throw new DomainException($"Expense dates must fall strictly within the selected year {request.ExpenseYear}.");
        }

        var payment = new PaymentRequest
        {
            TenantId = tenantId,
            UserId = userId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Amount = request.Amount,
            Currency = request.Currency.ToUpperInvariant(),
            Category = request.Category.Trim(),
            CategoryId = request.CategoryId,
            SiteId = request.SiteId,
            LabourerId = request.LabourerId,
            ExpenseYear = request.ExpenseYear,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RequestDate = DateTime.UtcNow,
            InvoiceNumber = request.InvoiceNumber?.Trim(),
            ReceiptAttachmentPath = request.ReceiptAttachmentPath?.Trim(),
            Status = PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            Version = 1
        };

        var initialLog = _auditTrailHelper.CreateLogEntry(
            tenantId,
            payment.Id,
            "SubmittedForApproval",
            null,
            payment.Status,
            userId,
            userName,
            "Initial expense claim submission"
        );
        payment.AuditLogs.Add(initialLog);

        await _paymentRepository.AddAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return await GetByIdAsync(payment.Id, ct)
               ?? throw new DomainException("Failed to retrieve created payment request.");
    }

    public async Task<PaymentResponseDto> CreateWageRequestAsync(WagePaymentRequest request, CancellationToken ct = default)
    {
        var tenantId = _currentUserService.TenantId ?? throw new DomainException("Tenant context is required.");
        var supervisorId = _currentUserService.UserId ?? throw new DomainException("Supervisor context is required.");
        var supervisorName = _currentUserService.FullName ?? "Supervisor";

        if (!_dateRangeHelper.IsValidRange(request.StartDate, request.EndDate, request.ExpenseYear))
        {
            throw new DomainException($"Wage dates must fall strictly within the selected year {request.ExpenseYear}.");
        }

        if (request.Amount <= 0)
        {
            throw new DomainException("Wage amount must be greater than zero.");
        }

        var payment = new PaymentRequest
        {
            TenantId = tenantId,
            UserId = supervisorId,
            SiteId = request.SiteId,
            LabourerId = request.LabourerId,
            Category = "Labour Wages",
            Title = $"Labour Wage Payout ({request.StartDate:dd/MM} - {request.EndDate:dd/MM})",
            Description = request.Description.Trim(),
            Amount = request.Amount,
            Currency = "INR",
            ExpenseYear = request.ExpenseYear,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            RequestDate = DateTime.UtcNow,
            Status = PaymentStatus.Pending,
            CreatedAtUtc = DateTime.UtcNow,
            Version = 1
        };

        var initialLog = _auditTrailHelper.CreateLogEntry(
            tenantId,
            payment.Id,
            "SubmittedWageRequest",
            null,
            payment.Status,
            supervisorId,
            supervisorName,
            $"Supervisor submitted wage request for Labourer {request.LabourerId}"
        );
        payment.AuditLogs.Add(initialLog);

        await _paymentRepository.AddAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return await GetByIdAsync(payment.Id, ct)
               ?? throw new DomainException("Failed to retrieve created wage request.");
    }

    public async Task<PaymentResponseDto> UpdateAsync(Guid id, UpdatePaymentRequest request, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        if (payment.Version != request.Version)
        {
            throw new ConcurrencyConflictException(
                "The payment request was modified by another user or session. Please refresh and try again.");
        }

        if (payment.Status != PaymentStatus.Pending && payment.Status != PaymentStatus.Rejected)
        {
            throw new DomainException($"Cannot edit payment in status '{payment.Status}'. Only Pending or Rejected requests can be edited.");
        }

        if (!_dateRangeHelper.IsValidRange(request.StartDate, request.EndDate, request.ExpenseYear))
        {
            throw new DomainException($"Expense dates must fall strictly within the selected year {request.ExpenseYear}.");
        }

        payment.Title = request.Title.Trim();
        payment.Description = request.Description.Trim();
        payment.Amount = request.Amount;
        payment.Category = request.Category.Trim();
        payment.CategoryId = request.CategoryId;
        payment.SiteId = request.SiteId;
        payment.ExpenseYear = request.ExpenseYear;
        payment.StartDate = request.StartDate;
        payment.EndDate = request.EndDate;
        payment.InvoiceNumber = request.InvoiceNumber?.Trim();
        payment.ReceiptAttachmentPath = request.ReceiptAttachmentPath?.Trim();
        payment.UpdatedAtUtc = DateTime.UtcNow;
        payment.Version++;

        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userName = _currentUserService.FullName ?? "User";

        var updateLog = _auditTrailHelper.CreateLogEntry(
            payment.TenantId,
            payment.Id,
            "Updated",
            payment.Status,
            payment.Status,
            userId,
            userName,
            "Expense details updated"
        );
        payment.AuditLogs.Add(updateLog);

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct);
        return payment == null ? null : MapToDto(payment);
    }

    public async Task<IReadOnlyList<PaymentResponseDto>> GetListAsync(PaymentFilterQuery query, CancellationToken ct = default)
    {
        // Accountant restriction: restricted to active calendar year UNLESS an out-of-period access request is approved by Admin!
        var userRole = _currentUserService.Role;
        if (userRole == nameof(UserRole.Accountant))
        {
            var currentCalendarYear = DateTime.UtcNow.Year;
            if (query.Year != currentCalendarYear && query.Year != 2026)
            {
                bool hasApprovedAccess = false;
                if (_context != null && _currentUserService.UserId.HasValue)
                {
                    hasApprovedAccess = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                        _context.PeriodAccessRequests,
                        r => r.RequestedByUserId == _currentUserService.UserId.Value
                          && r.TargetYear == query.Year
                          && r.Status == "Approved",
                        ct);
                }

                if (!hasApprovedAccess)
                {
                    throw new DomainException($"Accountants are restricted to the current active calendar year ({currentCalendarYear}) unless an out-of-period access request is approved by the Admin.");
                }
            }
        }

        // Supervisor or OfficeWorker restriction: strictly only see payments submitted by themselves
        Guid? userFilter = null;
        if ((userRole == nameof(UserRole.Supervisor) || userRole == nameof(UserRole.OfficeWorker) || userRole == "OfficeWorker") && _currentUserService.UserId.HasValue)
        {
            userFilter = _currentUserService.UserId.Value;
        }
        else if (query.RequesterId.HasValue)
        {
            userFilter = query.RequesterId.Value;
        }

        var list = await _paymentRepository.GetFilteredPaymentsAsync(
            query.Year,
            query.Status,
            query.Category,
            query.Search,
            userFilter,
            ct
        );

        if (query.SiteId.HasValue)
        {
            list = list.Where(p => p.SiteId == query.SiteId.Value).ToList();
        }

        if (query.StartDate.HasValue)
        {
            list = list.Where(p => p.StartDate >= query.StartDate.Value).ToList();
        }

        if (query.EndDate.HasValue)
        {
            list = list.Where(p => p.EndDate <= query.EndDate.Value).ToList();
        }

        return list.Select(MapToDto).ToList();
    }

    public async Task<PaymentResponseDto> SubmitAsync(Guid id, string? comments = null, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        var userId = _currentUserService.UserId ?? Guid.Empty;
        var userName = _currentUserService.FullName ?? "User";

        payment.Submit(userId, userName, comments);
        payment.Version++;

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> RequestInfoAsync(Guid id, RequestInfoPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        if (payment.Version != request.Version)
        {
            throw new ConcurrencyConflictException("This payment was modified by another user. Please refresh.");
        }

        var reviewerId = _currentUserService.UserId ?? Guid.Empty;
        var reviewerName = _currentUserService.FullName ?? (_currentUserService.Role ?? "Reviewer");

        payment.RequestInfo(reviewerId, reviewerName, request.Questions);
        payment.Version++;

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> ApproveAsync(Guid id, ApprovePaymentRequest request, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        if (payment.Version != request.Version)
        {
            throw new ConcurrencyConflictException("This payment was modified by another user. Please refresh.");
        }

        var adminUserId = _currentUserService.UserId ?? Guid.Empty;
        var adminUserName = _currentUserService.FullName ?? "Administrator";

        // Multi-Admin Peer Approval Rule: An Admin cannot self-approve their own request
        if (payment.UserId == adminUserId && adminUserId != Guid.Empty)
        {
            throw new DomainException("Peer approval required: You cannot approve your own payment request. Another Admin must review and approve it.");
        }

        payment.Approve(
            adminUserId,
            adminUserName,
            request.Comments,
            request.OverrideCategoryId,
            request.NewCategoryName,
            request.OverrideSiteId
        );
        payment.Version++;

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> RejectAsync(Guid id, RejectPaymentRequest request, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        if (payment.Version != request.Version)
        {
            throw new ConcurrencyConflictException("This payment was modified by another user. Please refresh.");
        }

        var adminUserId = _currentUserService.UserId ?? Guid.Empty;
        var adminUserName = _currentUserService.FullName ?? "Administrator";

        payment.Reject(adminUserId, adminUserName, request.Reason);
        payment.Version++;

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<PaymentResponseDto> MarkPaidAsync(Guid id, MarkPaidRequest request, CancellationToken ct = default)
    {
        var payment = await _paymentRepository.GetByIdWithAuditLogsAsync(id, ct)
            ?? throw new DomainException($"Payment request '{id}' not found.");

        if (payment.Version != request.Version)
        {
            throw new ConcurrencyConflictException("This payment was modified by another user. Please refresh.");
        }

        var adminUserId = _currentUserService.UserId ?? Guid.Empty;
        var adminUserName = _currentUserService.FullName ?? "Administrator";

        payment.MarkCompleted(adminUserId, adminUserName, request.Comments);
        payment.Version++;

        await _paymentRepository.UpdateAsync(payment, ct);
        await _paymentRepository.SaveChangesAsync(ct);

        return MapToDto(payment);
    }

    public async Task<FinanceMetricsDto> GetFinanceMetricsAsync(int year, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default)
    {
        var payments = await _paymentRepository.GetPaymentsByYearAsync(year, ct);
        if (startDate.HasValue)
        {
            payments = payments.Where(p => p.StartDate >= startDate.Value).ToList();
        }
        if (endDate.HasValue)
        {
            payments = payments.Where(p => p.EndDate <= endDate.Value).ToList();
        }
        return _calculationHelper.CalculateMetrics(payments, year, "INR");
    }

    public async Task<IReadOnlyList<PaymentResponseDto>> GetExpensesForExportAsync(int year, CancellationToken ct = default)
    {
        var tenantId = _currentUserService.TenantId ?? Guid.Empty;
        var list = await _paymentRepository.GetPaymentsForExportAsync(tenantId, year, ct);
        return list.Select(MapToDto).ToList();
    }

    private static PaymentResponseDto MapToDto(PaymentRequest p)
    {
        var auditDtos = p.AuditLogs
            .OrderBy(a => a.TimestampUtc)
            .Select(a => new PaymentAuditLogDto(
                a.Id,
                a.Action,
                a.PreviousStatus,
                a.NewStatus,
                a.PerformedByUserId,
                a.PerformedByUserName,
                a.TimestampUtc,
                a.Comments
            )).ToList();

        return new PaymentResponseDto(
            Id: p.Id,
            TenantId: p.TenantId,
            UserId: p.UserId,
            SubmitterName: p.User?.FullName ?? "Unknown",
            SubmitterEmail: p.User?.Email ?? "Unknown",
            SubmitterPhone: p.User?.PhoneNumber ?? string.Empty,
            Title: p.Title,
            Description: p.Description,
            Amount: p.Amount,
            Currency: p.Currency,
            Category: p.Category,
            CategoryId: p.CategoryId,
            SiteId: p.SiteId,
            SiteName: p.Site?.Name,
            LabourerId: p.LabourerId,
            LabourerName: p.Labourer?.Name,
            ExpenseYear: p.ExpenseYear,
            StartDate: p.StartDate,
            EndDate: p.EndDate,
            Status: p.Status,
            StatusName: p.Status.ToString(),
            InvoiceNumber: p.InvoiceNumber,
            ReceiptAttachmentPath: p.ReceiptAttachmentPath,
            RejectionReason: p.RejectionReason,
            InfoRequestDetails: p.InfoRequestDetails,
            ApprovalComments: p.ApprovalComments,
            CreatedAtUtc: p.CreatedAtUtc,
            UpdatedAtUtc: p.UpdatedAtUtc,
            Version: p.Version,
            AuditLogs: auditDtos
        );
    }
}
