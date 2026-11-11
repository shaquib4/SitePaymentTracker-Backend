using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class LabourersController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public LabourersController(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public record UpdateLabourerStatusRequest(bool IsActive);

    /// <summary>
    /// Gets all labourers for the tenant (or filtered to managed labourers for Supervisor).
    /// Includes rich supervisor mapping, who registered whom, site assignment, and aggregate payout metrics.
    /// Supports optional filtering by siteId and supervisorId.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedLabourersResponseDto>> GetLabourers(
        [FromQuery] Guid? siteId = null,
        [FromQuery] Guid? supervisorId = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool all = false,
        CancellationToken ct = default)
    {
        var role = _currentUserService.Role;
        var baseQuery = _context.Labourers
            .AsNoTracking()
            .AsQueryable();

        // Supervisors only see their managed labourers
        if (role == nameof(UserRole.Supervisor) && _currentUserService.UserId.HasValue)
        {
            baseQuery = baseQuery.Where(l => l.SupervisorId == _currentUserService.UserId.Value);
        }
        else if (supervisorId.HasValue)
        {
            baseQuery = baseQuery.Where(l => l.SupervisorId == supervisorId.Value);
        }

        if (siteId.HasValue)
        {
            baseQuery = baseQuery.Where(l => l.SiteId == siteId.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            baseQuery = baseQuery.Where(l =>
                (l.Name != null && l.Name.ToLower().Contains(s)) ||
                (l.Supervisor != null && l.Supervisor.FullName != null && l.Supervisor.FullName.ToLower().Contains(s)) ||
                (l.Site != null && l.Site.Name != null && l.Site.Name.ToLower().Contains(s)));
        }

        var totalCount = await baseQuery.CountAsync(ct);
        var activeCount = await baseQuery.CountAsync(l => l.IsActive, ct);
        var inactiveCount = totalCount - activeCount;

        var filteredIdsQuery = baseQuery.Select(l => l.Id);
        var summaryWageGroups = await _context.PaymentRequests
            .AsNoTracking()
            .Where(p => p.LabourerId.HasValue && filteredIdsQuery.Contains(p.LabourerId.Value))
            .GroupBy(p => p.Status)
            .Select(g => new
            {
                Status = g.Key,
                Amount = g.Sum(p => p.Amount),
                RequestedAmount = g.Sum(p => p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount)
            })
            .ToListAsync(ct);

        var totalClaimedPayouts = summaryWageGroups.Sum(g => g.RequestedAmount);
        var totalApprovedPayouts = summaryWageGroups
            .Where(g => g.Status == PaymentStatus.Approved || g.Status == PaymentStatus.Completed)
            .Sum(g => g.Amount);

        var effectivePage = all ? 1 : Math.Max(1, page);
        var effectivePageSize = all ? Math.Max(1, totalCount) : Math.Clamp(pageSize, 1, 200);
        var totalPages = totalCount == 0 ? 0 : (all ? 1 : (int)Math.Ceiling(totalCount / (double)effectivePageSize));

        var pagedLabourersQuery = baseQuery
            .Include(l => l.Supervisor)
            .Include(l => l.Site)
            .Include(l => l.CreatedByUser)
            .OrderBy(l => l.Name);

        var entities = all
            ? await pagedLabourersQuery.ToListAsync(ct)
            : await pagedLabourersQuery
                .Skip((effectivePage - 1) * effectivePageSize)
                .Take(effectivePageSize)
                .ToListAsync(ct);

        var pageLabourerIds = entities.Select(l => l.Id).ToList();
        var pageWages = pageLabourerIds.Count == 0
            ? []
            : await _context.PaymentRequests
                .AsNoTracking()
                .Where(p => p.LabourerId.HasValue && pageLabourerIds.Contains(p.LabourerId.Value))
                .Select(p => new
                {
                    LabourerId = p.LabourerId!.Value,
                    p.Status,
                    p.Amount,
                    p.RequestedAmount,
                    p.StartDate,
                    p.EndDate
                })
                .ToListAsync(ct);

        var wagesByLabourer = pageWages.ToLookup(w => w.LabourerId);

        var dtoList = entities.Select(l =>
        {
            var wages = wagesByLabourer[l.Id].ToList();
            var approvedPayments = wages
                .Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed)
                .ToList();

            var totalClaimed = wages.Sum(p => p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount);
            var totalApproved = approvedPayments.Sum(p => p.Amount);
            var totalDaysWorked = approvedPayments.Sum(p => Math.Max(1, (p.EndDate.DayNumber - p.StartDate.DayNumber) + 1));

            return new LabourerDto(
                Id: l.Id,
                Name: l.Name,
                DailyWageRate: l.DailyWageRate,
                PhotoUrl: l.PhotoUrl,
                IsActive: l.IsActive,
                CreatedAtUtc: l.CreatedAtUtc,
                SupervisorId: l.SupervisorId,
                SupervisorName: l.Supervisor?.FullName ?? "Unknown Supervisor",
                SupervisorPhone: l.Supervisor?.PhoneNumber ?? "",
                SupervisorSixDigitId: l.Supervisor?.SixDigitUserId ?? "",
                SiteId: l.SiteId,
                SiteName: l.Site?.Name ?? "Unassigned",
                CreatedByUserId: l.CreatedByUserId,
                CreatedByUserName: l.CreatedByUser?.FullName ?? l.Supervisor?.FullName ?? "System",
                CreatedByUserRole: l.CreatedByUser?.Role.ToString() ?? "Supervisor",
                TotalWagesClaimed: totalClaimed,
                TotalWagesApproved: totalApproved,
                TotalWageClaimsCount: wages.Count,
                PendingWageClaimsCount: wages.Count(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.InfoRequested),
                ApprovedWageClaimsCount: approvedPayments.Count,
                TotalDaysWorked: totalDaysWorked
            );
        }).ToList();

        return Ok(new PagedLabourersResponseDto(
            Items: dtoList,
            Page: effectivePage,
            PageSize: effectivePageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            Summary: new LabourerPagedSummaryDto(
                TotalCount: totalCount,
                ActiveCount: activeCount,
                InactiveCount: inactiveCount,
                TotalApprovedPayouts: totalApprovedPayouts,
                TotalClaimedPayouts: totalClaimedPayouts
            )
        ));
    }

    /// <summary>
    /// Onboards a new labourer.
    /// Supervisors can add labourers under themselves.
    /// Admins have the explicit authority to add labourers under ANY active supervisor.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<LabourerDto>> CreateLabourer([FromBody] CreateLabourerDto request, CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var currentUserId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();
        var role = _currentUserService.Role;

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Labourer name is required.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.DailyWageRate <= 0)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Daily wage rate must be greater than zero.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        Guid assignedSupervisorId;

        if (role == nameof(UserRole.Admin))
        {
            if (!request.SupervisorId.HasValue)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "As Admin, you must select which supervisor this labourer is assigned to.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            var supervisorExists = await _context.Users
                .AnyAsync(u => u.Id == request.SupervisorId.Value && u.TenantId == tenantId && u.IsActive, ct);

            if (!supervisorExists)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "The selected supervisor was not found or is inactive.",
                    Status = StatusCodes.Status400BadRequest
                });
            }

            assignedSupervisorId = request.SupervisorId.Value;
        }
        else
        {
            // Supervisor automatically assigns labourer to themselves
            assignedSupervisorId = currentUserId;
        }

        var labourer = new Labourer
        {
            TenantId = tenantId,
            SupervisorId = assignedSupervisorId,
            SiteId = request.SiteId,
            CreatedByUserId = currentUserId,
            Name = request.Name.Trim(),
            DailyWageRate = request.DailyWageRate,
            PhotoUrl = request.PhotoUrl,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Labourers.Add(labourer);
        await _context.SaveChangesAsync(ct);

        // Fetch populated entity for return DTO
        var created = await _context.Labourers
            .Include(l => l.Supervisor)
            .Include(l => l.Site)
            .Include(l => l.CreatedByUser)
            .FirstAsync(l => l.Id == labourer.Id, ct);

        var dto = new LabourerDto(
            Id: created.Id,
            Name: created.Name,
            DailyWageRate: created.DailyWageRate,
            PhotoUrl: created.PhotoUrl,
            IsActive: created.IsActive,
            CreatedAtUtc: created.CreatedAtUtc,
            SupervisorId: created.SupervisorId,
            SupervisorName: created.Supervisor?.FullName ?? "Unknown",
            SupervisorPhone: created.Supervisor?.PhoneNumber ?? "",
            SupervisorSixDigitId: created.Supervisor?.SixDigitUserId ?? "",
            SiteId: created.SiteId,
            SiteName: created.Site?.Name ?? "Unassigned",
            CreatedByUserId: created.CreatedByUserId,
            CreatedByUserName: created.CreatedByUser?.FullName ?? "Unknown",
            CreatedByUserRole: created.CreatedByUser?.Role.ToString() ?? "Unknown",
            TotalWagesClaimed: 0,
            TotalWagesApproved: 0,
            TotalWageClaimsCount: 0,
            PendingWageClaimsCount: 0,
            ApprovedWageClaimsCount: 0,
            TotalDaysWorked: 0
        );

        return CreatedAtAction(nameof(GetLabourers), new { id = labourer.Id }, dto);
    }

    /// <summary>
    /// Updates labourer details: assign/reassign to a new site, change wage rate, supervisor, or active status.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<LabourerDto>> UpdateLabourer(Guid id, [FromBody] UpdateLabourerDto request, CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var role = _currentUserService.Role;
        var currentUserId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();

        var labourer = await _context.Labourers
            .Include(l => l.Supervisor)
            .Include(l => l.Site)
            .Include(l => l.CreatedByUser)
            .Include(l => l.WagePayments)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

        if (labourer == null) return NotFound();

        if (role == nameof(UserRole.Supervisor) && labourer.SupervisorId != currentUserId)
        {
            return Forbid();
        }

        if (!string.IsNullOrWhiteSpace(request.Name))
        {
            labourer.Name = request.Name.Trim();
        }

        if (request.DailyWageRate > 0)
        {
            labourer.DailyWageRate = request.DailyWageRate;
        }

        if (request.PhotoUrl != null)
        {
            labourer.PhotoUrl = request.PhotoUrl;
        }

        if (request.IsActive.HasValue)
        {
            labourer.IsActive = request.IsActive.Value;
        }

        // Reassign to new Site
        labourer.SiteId = request.SiteId;

        // Admin can also reassign supervisor
        if (role == nameof(UserRole.Admin) && request.SupervisorId.HasValue)
        {
            var supervisorExists = await _context.Users
                .AnyAsync(u => u.Id == request.SupervisorId.Value && u.TenantId == tenantId && u.IsActive, ct);
            if (supervisorExists)
            {
                labourer.SupervisorId = request.SupervisorId.Value;
            }
        }

        await _context.SaveChangesAsync(ct);

        // Fetch refreshed entity with navigations
        var updated = await _context.Labourers
            .Include(l => l.Supervisor)
            .Include(l => l.Site)
            .Include(l => l.CreatedByUser)
            .Include(l => l.WagePayments)
            .FirstAsync(l => l.Id == id, ct);

        var approvedPayments = updated.WagePayments
            .Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed)
            .ToList();

        var dto = new LabourerDto(
            Id: updated.Id,
            Name: updated.Name,
            DailyWageRate: updated.DailyWageRate,
            PhotoUrl: updated.PhotoUrl,
            IsActive: updated.IsActive,
            CreatedAtUtc: updated.CreatedAtUtc,
            SupervisorId: updated.SupervisorId,
            SupervisorName: updated.Supervisor?.FullName ?? "Unknown",
            SupervisorPhone: updated.Supervisor?.PhoneNumber ?? "",
            SupervisorSixDigitId: updated.Supervisor?.SixDigitUserId ?? "",
            SiteId: updated.SiteId,
            SiteName: updated.Site?.Name ?? "Unassigned",
            CreatedByUserId: updated.CreatedByUserId,
            CreatedByUserName: updated.CreatedByUser?.FullName ?? "Unknown",
            CreatedByUserRole: updated.CreatedByUser?.Role.ToString() ?? "Unknown",
            TotalWagesClaimed: updated.WagePayments.Sum(p => p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount),
            TotalWagesApproved: approvedPayments.Sum(p => p.Amount),
            TotalWageClaimsCount: updated.WagePayments.Count,
            PendingWageClaimsCount: updated.WagePayments.Count(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.InfoRequested),
            ApprovedWageClaimsCount: approvedPayments.Count,
            TotalDaysWorked: approvedPayments.Sum(p => Math.Max(1, (p.EndDate.DayNumber - p.StartDate.DayNumber) + 1))
        );

        return Ok(dto);
    }

    /// <summary>
    /// Toggles active status of a labourer (Admin or Supervisor).
    /// </summary>
    [HttpPut("{id}/status")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<IActionResult> UpdateStatus(Guid id, [FromBody] UpdateLabourerStatusRequest request, CancellationToken ct)
    {
        var labourer = await _context.Labourers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (labourer == null) return NotFound();

        // Supervisors can only update their own labourers
        if (_currentUserService.Role == nameof(UserRole.Supervisor) && labourer.SupervisorId != _currentUserService.UserId)
        {
            return Forbid();
        }

        labourer.IsActive = request.IsActive;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Soft deletes a labourer (Admin only).
    /// Preserves full historical payment ledgers and audit records permanently.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteLabourer(Guid id, CancellationToken ct)
    {
        var labourer = await _context.Labourers.FirstOrDefaultAsync(l => l.Id == id, ct);
        if (labourer == null) return NotFound();

        labourer.IsDeleted = true;
        labourer.IsActive = false;
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Comprehensive Anti-Exploitation Wage Audit Ledger.
    /// Provides exact record of when the labourer received what amount,
    /// variance against registered daily wage rate, and supervisor over/under-claim analysis.
    /// </summary>
    [HttpGet("{id}/wage-history")]
    public async Task<ActionResult<LabourerWageAuditResponse>> GetWageHistory(Guid id, CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var labourer = await _context.Labourers
            .IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId && l.Id == id)
            .Include(l => l.Supervisor)
            .Include(l => l.CreatedByUser)
            .Include(l => l.WagePayments)
                .ThenInclude(p => p.AuditLogs)
            .Include(l => l.WagePayments)
                .ThenInclude(p => p.Site)
            .Include(l => l.WagePayments)
                .ThenInclude(p => p.User)
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        if (labourer == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Not Found",
                Detail = $"Labourer with ID {id} was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        // Supervisors can only inspect their own labourers
        if (_currentUserService.Role == nameof(UserRole.Supervisor) && labourer.SupervisorId != _currentUserService.UserId)
        {
            return Forbid();
        }

        var payouts = new List<LabourerWagePayoutItemDto>();

        foreach (var p in labourer.WagePayments.OrderByDescending(p => p.CreatedAtUtc))
        {
            var claimDays = Math.Max(1, (p.EndDate.DayNumber - p.StartDate.DayNumber) + 1);
            var expectedWage = claimDays * labourer.DailyWageRate;
            var variance = p.Amount - expectedWage;
            var hasDiscrepancy = Math.Abs(variance) > 0.01m;

            var approvalLog = p.AuditLogs
                .Where(a => a.NewStatus == PaymentStatus.Approved)
                .OrderByDescending(a => a.TimestampUtc)
                .FirstOrDefault();

            payouts.Add(new LabourerWagePayoutItemDto(
                PaymentId: p.Id,
                Title: p.Title,
                Description: p.Description,
                SiteId: p.SiteId,
                SiteName: p.Site?.Name,
                StartDate: p.StartDate,
                EndDate: p.EndDate,
                ClaimDays: claimDays,
                DailyWageRate: labourer.DailyWageRate,
                ExpectedWage: expectedWage,
                ActualAmount: p.Amount,
                Variance: variance,
                HasDiscrepancy: hasDiscrepancy,
                Status: p.Status.ToString(),
                RequestDate: p.CreatedAtUtc,
                RequestedBySupervisorName: p.User?.FullName ?? labourer.Supervisor?.FullName ?? "Supervisor",
                RequestedBySupervisorPhone: p.User?.PhoneNumber ?? labourer.Supervisor?.PhoneNumber ?? "",
                ApprovalDate: approvalLog?.TimestampUtc,
                ApprovedByUserName: approvalLog?.PerformedByUserName,
                ApprovalComments: p.ApprovalComments,
                RejectionReason: p.RejectionReason,
                RequestedAmount: p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount
            ));
        }

        var totalClaimedAmount = payouts.Sum(p => p.ActualAmount);
        var totalApprovedAmount = payouts
            .Where(p => p.Status == PaymentStatus.Approved.ToString() || p.Status == PaymentStatus.Completed.ToString())
            .Sum(p => p.ActualAmount);
        var totalPendingAmount = payouts
            .Where(p => p.Status == PaymentStatus.Pending.ToString() || p.Status == PaymentStatus.InfoRequested.ToString())
            .Sum(p => p.ActualAmount);
        var totalPaidAmount = payouts
            .Where(p => p.Status == PaymentStatus.Completed.ToString())
            .Sum(p => p.ActualAmount);

        var totalDaysClaimed = payouts.Sum(p => p.ClaimDays);
        var expectedTotalWages = totalDaysClaimed * labourer.DailyWageRate;
        var totalVariance = totalClaimedAmount - expectedTotalWages;

        string exploitationRisk;
        string exploitationRiskDesc;

        if (payouts.Count == 0)
        {
            exploitationRisk = "No History";
            exploitationRiskDesc = "No wage claims have been submitted by any supervisor for this labourer yet.";
        }
        else if (Math.Abs(totalVariance) <= 0.01m)
        {
            exploitationRisk = "Clean / Verified";
            exploitationRiskDesc = $"All {payouts.Count} wage claims strictly match the registered daily rate of ₹{labourer.DailyWageRate:N0}/day for {totalDaysClaimed} total days.";
        }
        else if (totalVariance > 0)
        {
            exploitationRisk = "Over-Claim Anomaly";
            exploitationRiskDesc = $"Supervisor has claimed ₹{totalVariance:N0} MORE than expected based on {totalDaysClaimed} days at ₹{labourer.DailyWageRate:N0}/day. Audit supervisor immediately to prevent fund diversion.";
        }
        else
        {
            exploitationRisk = "Under-Payment Risk";
            exploitationRiskDesc = $"Supervisor has claimed ₹{Math.Abs(totalVariance):N0} LESS than the full daily rate for {totalDaysClaimed} days. Confirm whether this labourer is working part-time or being underpaid.";
        }

        var response = new LabourerWageAuditResponse(
            LabourerId: labourer.Id,
            Name: labourer.Name,
            DailyWageRate: labourer.DailyWageRate,
            PhotoUrl: labourer.PhotoUrl,
            IsActive: labourer.IsActive,
            CreatedAtUtc: labourer.CreatedAtUtc,
            SupervisorId: labourer.SupervisorId,
            SupervisorName: labourer.Supervisor?.FullName ?? "Unknown",
            SupervisorPhone: labourer.Supervisor?.PhoneNumber ?? "",
            SupervisorSixDigitId: labourer.Supervisor?.SixDigitUserId ?? "",
            CreatedByUserId: labourer.CreatedByUserId,
            CreatedByUserName: labourer.CreatedByUser?.FullName ?? labourer.Supervisor?.FullName ?? "Unknown",
            CreatedByUserRole: labourer.CreatedByUser?.Role.ToString() ?? "Supervisor",
            TotalApprovedAmount: totalApprovedAmount,
            TotalPendingAmount: totalPendingAmount,
            TotalPaidAmount: totalPaidAmount,
            TotalClaimedAmount: totalClaimedAmount,
            TotalDaysClaimed: totalDaysClaimed,
            ExpectedTotalWages: expectedTotalWages,
            TotalVariance: totalVariance,
            ExploitationRisk: exploitationRisk,
            ExploitationRiskDescription: exploitationRiskDesc,
            Payouts: payouts
        );

        return Ok(response);
    }
}
