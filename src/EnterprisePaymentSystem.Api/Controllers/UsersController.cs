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
public class UsersController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUserService _currentUserService;

    public UsersController(
        IApplicationDbContext context,
        IPasswordHasher passwordHasher,
        ICurrentUserService currentUserService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _currentUserService = currentUserService;
    }

    public record GenerateUserRequest(
        string FullName,
        string? Email,
        string PhoneNumber,
        UserRole Role,
        string Department
    );

    public record GeneratedUserResponse(
        Guid Id,
        string FullName,
        string? Email,
        string PhoneNumber,
        string SixDigitUserId,
        string TemporaryPassword,
        UserRole Role,
        string Department
    );

    public record UserListItemDto(
        Guid Id,
        string FullName,
        string? Email,
        string PhoneNumber,
        string SixDigitUserId,
        UserRole Role,
        string Department,
        bool IsActive,
        DateTime CreatedAtUtc
    );

    public record PagedUsersResponseDto(
        IReadOnlyList<UserListItemDto> Items,
        int Page,
        int PageSize,
        int TotalCount,
        int TotalPages
    );

    public record UserHistoryResponse(
        Guid UserId,
        string FullName,
        string? Email,
        string PhoneNumber,
        string SixDigitUserId,
        UserRole Role,
        string Department,
        bool IsActive,
        decimal TotalApprovedAmount,
        decimal TotalPendingAmount,
        int TotalRequestsCount,
        IReadOnlyList<UserHistoryItemDto> History,
        decimal GrandTotalAmount = 0m,
        int FilteredCount = 0,
        int Page = 1,
        int PageSize = 20,
        int TotalPages = 0,
        IReadOnlyList<CategoryBreakdownDto>? CategoryBreakdowns = null
    );

    public record UserHistoryItemDto(
        Guid Id,
        string Title,
        string Description,
        decimal Amount,
        string Category,
        Guid? SiteId,
        string? SiteName,
        string? LabourerName,
        PaymentStatus Status,
        DateOnly StartDate,
        DateOnly EndDate,
        DateTime RequestDate,
        DateTime? ApprovalDate,
        string? ApprovalComments,
        uint Version,
        decimal RequestedAmount = 0m,
        decimal? ApprovedAmount = null
    );

    [HttpGet]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<ActionResult<PagedUsersResponseDto>> GetUsers(
        [FromQuery] UserRole? role = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] bool all = false,
        CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        var query = _context.Users.AsNoTracking().AsQueryable();

        // Admin should not see himself in the employees list
        if (currentUserId.HasValue)
        {
            query = query.Where(u => u.Id != currentUserId.Value);
        }

        if (role.HasValue)
        {
            query = query.Where(u => u.Role == role.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(u =>
                (u.FullName != null && u.FullName.ToLower().Contains(s)) ||
                (u.Email != null && u.Email.ToLower().Contains(s)) ||
                (u.PhoneNumber != null && u.PhoneNumber.ToLower().Contains(s)) ||
                (u.SixDigitUserId != null && u.SixDigitUserId.ToLower().Contains(s)) ||
                (u.Department != null && u.Department.ToLower().Contains(s)));
        }

        var totalCount = await query.CountAsync(ct);

        var ordered = query.OrderBy(u => u.FullName);

        int effectivePage = all ? 1 : Math.Max(1, page);
        int effectivePageSize = all ? Math.Max(1, totalCount) : Math.Clamp(pageSize, 1, 200);
        int totalPages = totalCount == 0 ? 0 : (all ? 1 : (int)Math.Ceiling(totalCount / (double)effectivePageSize));

        var pagedQuery = all
            ? ordered
            : ordered.Skip((effectivePage - 1) * effectivePageSize).Take(effectivePageSize);

        var users = await pagedQuery
            .Select(u => new UserListItemDto(
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.SixDigitUserId,
                u.Role,
                u.Department,
                u.IsActive,
                u.CreatedAtUtc
            ))
            .ToListAsync(ct);

        return Ok(new PagedUsersResponseDto(
            Items: users,
            Page: effectivePage,
            PageSize: effectivePageSize,
            TotalCount: totalCount,
            TotalPages: totalPages
        ));
    }

    /// <summary>
    /// Admin generates a new employee with a 6-digit ID and temporary password.
    /// Admin can copy credentials to WhatsApp/SMS.
    /// Email is optional for non-Admin employees (Supervisor, Accountant, Vendor).
    /// </summary>
    [HttpPost("generate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<GeneratedUserResponse>> GenerateUser(
        [FromBody] GenerateUserRequest request,
        CancellationToken ct)
    {
        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();

        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Phone number is mandatory so Admins can call for verification.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (request.Role == UserRole.Admin && string.IsNullOrWhiteSpace(request.Email))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Email is mandatory for Admin accounts.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        string? normalizedEmail = string.IsNullOrWhiteSpace(request.Email)
            ? null
            : request.Email.Trim().ToLowerInvariant();

        if (normalizedEmail != null)
        {
            var exists = await _context.Users.IgnoreQueryFilters()
                .AnyAsync(u => u.TenantId == tenantId && u.Email == normalizedEmail, ct);
            if (exists)
            {
                return BadRequest(new ProblemDetails
                {
                    Title = "Validation Error",
                    Detail = "A user with this email address already exists.",
                    Status = StatusCodes.Status400BadRequest
                });
            }
        }

        // Generate a random 6-digit User ID (e.g., 592314)
        var random = new Random();
        string sixDigitId;
        do
        {
            sixDigitId = random.Next(100000, 999999).ToString();
        } while (await _context.Users.IgnoreQueryFilters().AnyAsync(u => u.TenantId == tenantId && u.SixDigitUserId == sixDigitId, ct));

        // Generate temporary password
        var temporaryPassword = $"Build@{random.Next(1000, 9999)}!";
        var passwordHash = _passwordHasher.HashPassword(temporaryPassword);

        var newUser = new User
        {
            TenantId = tenantId,
            FullName = request.FullName.Trim(),
            Email = normalizedEmail,
            PhoneNumber = request.PhoneNumber.Trim(),
            SixDigitUserId = sixDigitId,
            Role = request.Role,
            Department = request.Department.Trim(),
            PasswordHash = passwordHash,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Users.Add(newUser);
        await _context.SaveChangesAsync(ct);

        return Ok(new GeneratedUserResponse(
            newUser.Id,
            newUser.FullName,
            newUser.Email,
            newUser.PhoneNumber,
            newUser.SixDigitUserId,
            temporaryPassword,
            newUser.Role,
            newUser.Department
        ));
    }

    [HttpPut("{id:guid}/deactivate")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return NotFound();

        user.IsActive = !user.IsActive;
        await _context.SaveChangesAsync(ct);
        return Ok(new { user.Id, user.IsActive });
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return NotFound();

        _context.Users.Remove(user);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// User history: Lifetime aggregate profile of employee.
    /// Admin can view any user; Employees/OfficeWorkers can strictly view their own profile.
    /// Total Approved (₹), Total Pending (₹), and all historical requests with period filter.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [Authorize]
    public async Task<ActionResult<UserHistoryResponse>> GetUserHistory(
        Guid id,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        [FromQuery] Guid? siteId = null,
        [FromQuery] PaymentStatus? status = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var currentUserId = _currentUserService.UserId;
        var currentRole = _currentUserService.Role;

        // Admin can view any user's history; non-admins can strictly only view their own history!
        if (currentRole != nameof(UserRole.Admin) && currentUserId != id)
        {
            return Forbid();
        }

        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var periodBaseQuery = _context.PaymentRequests
            .AsNoTracking()
            .Where(p => p.UserId == id);

        if (startDate.HasValue)
        {
            periodBaseQuery = periodBaseQuery.Where(p => p.StartDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            periodBaseQuery = periodBaseQuery.Where(p => p.EndDate <= endDate.Value);
        }

        var totalRequestsCount = await periodBaseQuery.CountAsync(ct);

        var queryable = periodBaseQuery;
        if (siteId.HasValue)
        {
            queryable = queryable.Where(p => p.SiteId == siteId.Value);
        }

        if (status.HasValue)
        {
            queryable = queryable.Where(p => p.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            queryable = queryable.Where(p =>
                (p.Title != null && p.Title.ToLower().Contains(s)) ||
                (p.Category != null && p.Category.ToLower().Contains(s)) ||
                (p.Description != null && p.Description.ToLower().Contains(s)) ||
                (p.Site != null && p.Site.Name != null && p.Site.Name.ToLower().Contains(s)));
        }

        var statusGroups = await queryable
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(p => p.Amount) })
            .ToListAsync(ct);

        var totalApproved = statusGroups
            .Where(g => g.Status == PaymentStatus.Approved || g.Status == PaymentStatus.Completed)
            .Sum(g => g.Amount);

        var totalPending = statusGroups
            .Where(g => g.Status == PaymentStatus.Pending || g.Status == PaymentStatus.InfoRequested)
            .Sum(g => g.Amount);

        var grandTotal = statusGroups.Sum(g => g.Amount);
        var filteredCount = statusGroups.Sum(g => g.Count);
        var approvedOrCompletedCount = statusGroups
            .Where(g => g.Status == PaymentStatus.Approved || g.Status == PaymentStatus.Completed)
            .Sum(g => g.Count);

        var pieQuery = queryable;
        if (!status.HasValue && approvedOrCompletedCount > 0)
        {
            pieQuery = pieQuery.Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed);
        }

        var rawCategoryGroups = await pieQuery
            .GroupBy(p => p.Category)
            .Select(g => new { Category = g.Key, Count = g.Count(), TotalAmount = g.Sum(p => p.Amount) })
            .OrderByDescending(c => c.TotalAmount)
            .ToListAsync(ct);

        var categoryBreakdowns = rawCategoryGroups
            .Select(c => new CategoryBreakdownDto(
                string.IsNullOrWhiteSpace(c.Category) ? "General" : c.Category,
                c.Count,
                c.TotalAmount))
            .ToList();

        var totalPages = filteredCount == 0 ? 0 : (int)Math.Ceiling(filteredCount / (double)pageSize);

        var payments = await queryable
            .OrderByDescending(p => p.RequestDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.Site)
            .Include(p => p.Labourer)
            .Include(p => p.AuditLogs)
            .ToListAsync(ct);

        var historyItems = payments.Select(p =>
        {
            var approvalLog = p.AuditLogs
                .OrderByDescending(a => a.TimestampUtc)
                .FirstOrDefault(a => a.NewStatus == PaymentStatus.Approved);

            return new UserHistoryItemDto(
                p.Id,
                p.Title,
                p.Description,
                p.Amount,
                p.Category,
                p.SiteId,
                p.Site?.Name,
                p.Labourer?.Name,
                p.Status,
                p.StartDate,
                p.EndDate,
                p.RequestDate,
                approvalLog?.TimestampUtc,
                p.ApprovalComments,
                p.Version,
                p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount,
                (p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed) ? p.Amount : null
            );
        }).ToList();

        return Ok(new UserHistoryResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.PhoneNumber,
            user.SixDigitUserId,
            user.Role,
            user.Department,
            user.IsActive,
            totalApproved,
            totalPending,
            totalRequestsCount,
            historyItems,
            GrandTotalAmount: grandTotal,
            FilteredCount: filteredCount,
            Page: page,
            PageSize: pageSize,
            TotalPages: totalPages,
            CategoryBreakdowns: categoryBreakdowns
        ));
    }
}

