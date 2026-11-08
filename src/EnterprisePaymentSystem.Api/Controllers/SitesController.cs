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
public class SitesController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public SitesController(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public record CreateSiteRequest(string Name, string Location, decimal? ContractValue = null);

    public record UploadMbRequest(int Month, int Year, decimal TotalEstimatedValue, string? ExcelFileUrl);

    public record RecordSiteSettlementRequest(
        decimal FinalPaymentReceived,
        decimal? ContractValue = null,
        bool IsSettled = true
    );

    public record SiteProfitabilityDto(
        Guid SiteId,
        string SiteName,
        string Location,
        decimal? FinalPaymentReceived,
        decimal? ContractValue,
        bool IsSettled,
        DateTime? SettledAtUtc,
        decimal MbTotalEstimatedValue,
        bool HasMbUploaded,
        decimal TotalApprovedExpenses,
        decimal TotalPendingExpenses,
        decimal CurrentMargin,
        double ProfitMarginPercentage,
        string SettlementStatus,
        int ExpenseClaimsCount
    );

    public record SiteExpenseItemDto(
        Guid Id,
        string Title,
        string Description,
        string Category,
        decimal Amount,
        string Status,
        DateOnly StartDate,
        DateOnly EndDate,
        DateTime CreatedAtUtc,
        Guid UserId,
        string SubmitterName,
        string SubmitterPhone,
        Guid? LabourerId,
        string? LabourerName,
        DateTime? ApprovedAtUtc,
        string? ApprovedByUserName,
        string? ApprovalComments,
        uint Version = 1,
        Guid? CategoryId = null,
        string? InfoRequestDetails = null
    );

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Site>>> GetSites(CancellationToken ct)
    {
        var list = await _context.Sites
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        return Ok(list);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<Site>> CreateSite([FromBody] CreateSiteRequest request, CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();

        var site = new Site
        {
            TenantId = tenantId,
            Name = request.Name.Trim(),
            Location = request.Location.Trim(),
            ContractValue = request.ContractValue,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Sites.Add(site);
        await _context.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetSites), new { id = site.Id }, site);
    }

    /// <summary>
    /// Site Profitability View:
    /// Final Profit or Loss is strictly finalized when the Final Payment / Final MB is received by Admin.
    /// MB is not mandatory; Admin can record Final Payment received directly.
    /// </summary>
    [HttpGet("profitability")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<ActionResult<IEnumerable<SiteProfitabilityDto>>> GetProfitability(
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var sites = await _context.Sites
            .Include(s => s.MeasurementBooks)
            .Include(s => s.PaymentRequests)
            .Where(s => s.IsActive)
            .ToListAsync(ct);

        var list = sites.Select(s =>
        {
            var payments = s.PaymentRequests.AsEnumerable();
            if (startDate.HasValue) payments = payments.Where(p => p.StartDate >= startDate.Value);
            if (endDate.HasValue) payments = payments.Where(p => p.EndDate <= endDate.Value);
            var paymentsList = payments.ToList();

            var mbTotal = s.MeasurementBooks.Sum(m => m.TotalEstimatedValue);
            var hasMb = s.MeasurementBooks.Any();

            var approvedExpenses = paymentsList
                .Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed)
                .Sum(p => p.Amount);

            var pendingExpenses = paymentsList
                .Where(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.InfoRequested)
                .Sum(p => p.Amount);

            decimal revenueBase;
            string status;

            if (s.IsSettled && s.FinalPaymentReceived.HasValue)
            {
                revenueBase = s.FinalPaymentReceived.Value;
                status = "Finalized Settlement (Payment Received)";
            }
            else if (s.FinalPaymentReceived.HasValue)
            {
                revenueBase = s.FinalPaymentReceived.Value;
                status = "Final Payment Received (Pending Closeout)";
            }
            else if (hasMb && mbTotal > 0)
            {
                revenueBase = mbTotal;
                status = "Ongoing (MB Valuation Estimate)";
            }
            else if (s.ContractValue.HasValue && s.ContractValue.Value > 0)
            {
                revenueBase = s.ContractValue.Value;
                status = "Ongoing (Contract Value Estimate)";
            }
            else
            {
                revenueBase = 0;
                status = "Ongoing (Awaiting Final Payment / MB)";
            }

            var margin = revenueBase > 0 ? (revenueBase - approvedExpenses) : -approvedExpenses;
            var marginPercent = revenueBase > 0 ? (double)(margin / revenueBase) * 100.0 : 0.0;

            return new SiteProfitabilityDto(
                SiteId: s.Id,
                SiteName: s.Name,
                Location: s.Location,
                FinalPaymentReceived: s.FinalPaymentReceived,
                ContractValue: s.ContractValue,
                IsSettled: s.IsSettled,
                SettledAtUtc: s.SettledAtUtc,
                MbTotalEstimatedValue: mbTotal,
                HasMbUploaded: hasMb,
                TotalApprovedExpenses: approvedExpenses,
                TotalPendingExpenses: pendingExpenses,
                CurrentMargin: margin,
                ProfitMarginPercentage: Math.Round(marginPercent, 2),
                SettlementStatus: status,
                ExpenseClaimsCount: paymentsList.Count
            );
        }).OrderByDescending(s => s.TotalApprovedExpenses).ToList();

        return Ok(list);
    }

    /// <summary>
    /// Admin records the Final Payment received from the client for this site
    /// or marks the site as settled/finalized.
    /// </summary>
    [HttpPut("{siteId:guid}/settlement")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SettleSite(
        Guid siteId,
        [FromBody] RecordSiteSettlementRequest request,
        CancellationToken ct)
    {
        var site = await _context.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site == null) return NotFound("Site not found.");

        site.FinalPaymentReceived = request.FinalPaymentReceived;
        if (request.ContractValue.HasValue)
        {
            site.ContractValue = request.ContractValue.Value;
        }
        site.IsSettled = request.IsSettled;
        site.SettledAtUtc = request.IsSettled ? DateTime.UtcNow : null;

        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Gets all itemized expense records for a specific site, with optional date and category filters.
    /// </summary>
    [HttpGet("{siteId:guid}/expenses")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<ActionResult<IEnumerable<SiteExpenseItemDto>>> GetSiteExpenses(
        Guid siteId,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        var site = await _context.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site == null) return NotFound("Site not found.");

        var query = _context.PaymentRequests
            .Include(p => p.User)
            .Include(p => p.Labourer)
            .Include(p => p.AuditLogs)
            .Where(p => p.SiteId == siteId);

        if (startDate.HasValue) query = query.Where(p => p.StartDate >= startDate.Value);
        if (endDate.HasValue) query = query.Where(p => p.EndDate <= endDate.Value);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(p => p.Category == category);

        var list = await query
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);

        var dtos = list.Select(p =>
        {
            var approvalLog = p.AuditLogs
                .Where(a => a.NewStatus == PaymentStatus.Approved)
                .OrderByDescending(a => a.TimestampUtc)
                .FirstOrDefault();

            return new SiteExpenseItemDto(
                Id: p.Id,
                Title: p.Title,
                Description: p.Description,
                Category: p.Category,
                Amount: p.Amount,
                Status: p.Status.ToString(),
                StartDate: p.StartDate,
                EndDate: p.EndDate,
                CreatedAtUtc: p.CreatedAtUtc,
                UserId: p.UserId,
                SubmitterName: p.User?.FullName ?? "Unknown",
                SubmitterPhone: p.User?.PhoneNumber ?? "",
                LabourerId: p.LabourerId,
                LabourerName: p.Labourer?.Name,
                ApprovedAtUtc: approvalLog?.TimestampUtc,
                ApprovedByUserName: approvalLog?.PerformedByUserName,
                ApprovalComments: p.ApprovalComments,
                Version: p.Version,
                CategoryId: p.CategoryId,
                InfoRequestDetails: p.InfoRequestDetails
            );
        }).ToList();

        return Ok(dtos);
    }

    /// <summary>
    /// Admin uploads Measurement Book (MB) estimated values for a site/month to compare against expenses.
    /// MB is optional, but available if the contractor wants detailed monthly measurement comparison.
    /// </summary>
    [HttpPost("{siteId:guid}/mb-upload")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UploadMeasurementBook(
        Guid siteId,
        [FromBody] UploadMbRequest request,
        CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var site = await _context.Sites.FirstOrDefaultAsync(s => s.Id == siteId, ct);
        if (site == null) return NotFound("Site not found.");

        var mb = new MeasurementBook
        {
            TenantId = tenantId,
            SiteId = siteId,
            Month = request.Month,
            Year = request.Year,
            TotalEstimatedValue = request.TotalEstimatedValue,
            ExcelFileUrl = request.ExcelFileUrl,
            UploadedAt = DateTime.UtcNow
        };

        _context.MeasurementBooks.Add(mb);
        await _context.SaveChangesAsync(ct);

        return Ok(mb);
    }
}
