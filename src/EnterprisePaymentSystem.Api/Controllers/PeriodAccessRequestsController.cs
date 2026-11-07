using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/period-access")]
[Authorize]
public class PeriodAccessRequestsController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public PeriodAccessRequestsController(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public record CreatePeriodAccessRequestDto(int TargetYear, string Reason);
    public record ReviewPeriodAccessRequestDto(bool Approve, string? AdminNotes);

    public record PeriodAccessResponseDto(
        Guid Id,
        int TargetYear,
        string Reason,
        string Status,
        DateTime CreatedAtUtc,
        Guid RequestedByUserId,
        string RequestedByUserName,
        string RequestedByUserEmail,
        Guid? ReviewedByUserId,
        string? ReviewedByUserName,
        DateTime? ReviewedAtUtc,
        string? AdminNotes
    );

    /// <summary>
    /// Accountant submits an out-of-bounds historical year access request.
    /// </summary>
    [HttpPost("request")]
    [Authorize(Roles = "Accountant")]
    public async Task<ActionResult<PeriodAccessResponseDto>> RequestPeriodAccess(
        [FromBody] CreatePeriodAccessRequestDto dto,
        CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();

        if (dto.TargetYear < 2000 || dto.TargetYear > 2100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Target year must be valid.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (string.IsNullOrWhiteSpace(dto.Reason))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "A business justification/reason is required for historical year access.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        // Check if existing pending or approved request already exists
        var existing = await _context.PeriodAccessRequests
            .FirstOrDefaultAsync(r => r.RequestedByUserId == userId && r.TargetYear == dto.TargetYear && r.Status != "Rejected", ct);

        if (existing != null)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Conflict",
                Detail = $"An access request for year {dto.TargetYear} is already {existing.Status.ToLower()}.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var request = new PeriodAccessRequest
        {
            TenantId = tenantId,
            RequestedByUserId = userId,
            TargetYear = dto.TargetYear,
            Reason = dto.Reason.Trim(),
            Status = "Pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.PeriodAccessRequests.Add(request);
        await _context.SaveChangesAsync(ct);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId, ct);

        return Ok(new PeriodAccessResponseDto(
            request.Id,
            request.TargetYear,
            request.Reason,
            request.Status,
            request.CreatedAtUtc,
            userId,
            user?.FullName ?? "Accountant",
            user?.Email ?? "",
            null,
            null,
            null,
            null
        ));
    }

    /// <summary>
    /// Gets all accessible calendar years for the logged-in user.
    /// Admin gets 2024..2026.
    /// Accountant gets Current Year (2026) + any approved historical years.
    /// </summary>
    [HttpGet("accessible-years")]
    public async Task<ActionResult<IEnumerable<int>>> GetAccessibleYears(CancellationToken ct)
    {
        var role = _currentUserService.Role;
        var currentYear = DateTime.UtcNow.Year;

        if (role == nameof(UserRole.Admin))
        {
            return Ok(new[] { 2026, 2025, 2024 });
        }

        if (role == nameof(UserRole.Accountant) && _currentUserService.UserId.HasValue)
        {
            var approvedYears = await _context.PeriodAccessRequests
                .Where(r => r.RequestedByUserId == _currentUserService.UserId.Value && r.Status == "Approved")
                .Select(r => r.TargetYear)
                .Distinct()
                .ToListAsync(ct);

            var years = new HashSet<int>(approvedYears) { currentYear, 2026 };
            return Ok(years.OrderByDescending(y => y));
        }

        return Ok(new[] { currentYear, 2026 });
    }

    /// <summary>
    /// Accountant queries their own period access requests and statuses.
    /// </summary>
    [HttpGet("my-requests")]
    [Authorize(Roles = "Accountant")]
    public async Task<ActionResult<IEnumerable<PeriodAccessResponseDto>>> GetMyRequests(CancellationToken ct)
    {
        var userId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();

        var requests = await _context.PeriodAccessRequests
            .Include(r => r.RequestedByUser)
            .Include(r => r.ReviewedByUser)
            .Where(r => r.RequestedByUserId == userId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        var dtos = requests.Select(r => new PeriodAccessResponseDto(
            r.Id,
            r.TargetYear,
            r.Reason,
            r.Status,
            r.CreatedAtUtc,
            r.RequestedByUserId,
            r.RequestedByUser?.FullName ?? "Accountant",
            r.RequestedByUser?.Email ?? "",
            r.ReviewedByUserId,
            r.ReviewedByUser?.FullName,
            r.ReviewedAtUtc,
            r.AdminNotes
        ));

        return Ok(dtos);
    }

    /// <summary>
    /// Admin retrieves all pending period access requests across accountants.
    /// </summary>
    [HttpGet("pending")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IEnumerable<PeriodAccessResponseDto>>> GetPendingRequests(CancellationToken ct)
    {
        var requests = await _context.PeriodAccessRequests
            .Include(r => r.RequestedByUser)
            .Include(r => r.ReviewedByUser)
            .OrderByDescending(r => r.CreatedAtUtc)
            .ToListAsync(ct);

        var dtos = requests.Select(r => new PeriodAccessResponseDto(
            r.Id,
            r.TargetYear,
            r.Reason,
            r.Status,
            r.CreatedAtUtc,
            r.RequestedByUserId,
            r.RequestedByUser?.FullName ?? "Accountant",
            r.RequestedByUser?.Email ?? "",
            r.ReviewedByUserId,
            r.ReviewedByUser?.FullName,
            r.ReviewedAtUtc,
            r.AdminNotes
        ));

        return Ok(dtos);
    }

    /// <summary>
    /// Admin reviews (approves or rejects) an accountant's period access request.
    /// </summary>
    [HttpPost("{id:guid}/review")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PeriodAccessResponseDto>> ReviewRequest(
        Guid id,
        [FromBody] ReviewPeriodAccessRequestDto dto,
        CancellationToken ct)
    {
        var adminId = _currentUserService.UserId ?? throw new UnauthorizedAccessException();

        var request = await _context.PeriodAccessRequests
            .Include(r => r.RequestedByUser)
            .FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request == null) return NotFound();

        request.Status = dto.Approve ? "Approved" : "Rejected";
        request.ReviewedByUserId = adminId;
        request.ReviewedAtUtc = DateTime.UtcNow;
        request.AdminNotes = dto.AdminNotes;

        await _context.SaveChangesAsync(ct);

        var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == adminId, ct);

        return Ok(new PeriodAccessResponseDto(
            request.Id,
            request.TargetYear,
            request.Reason,
            request.Status,
            request.CreatedAtUtc,
            request.RequestedByUserId,
            request.RequestedByUser?.FullName ?? "Accountant",
            request.RequestedByUser?.Email ?? "",
            adminId,
            adminUser?.FullName,
            request.ReviewedAtUtc,
            request.AdminNotes
        ));
    }
}

