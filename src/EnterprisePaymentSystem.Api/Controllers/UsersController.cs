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
        string Email,
        string PhoneNumber,
        UserRole Role,
        string Department
    );

    public record GeneratedUserResponse(
        Guid Id,
        string FullName,
        string Email,
        string PhoneNumber,
        string SixDigitUserId,
        string TemporaryPassword,
        UserRole Role,
        string Department
    );

    public record UserHistoryResponse(
        Guid UserId,
        string FullName,
        string Email,
        string PhoneNumber,
        string SixDigitUserId,
        UserRole Role,
        string Department,
        bool IsActive,
        decimal TotalApprovedAmount,
        decimal TotalPendingAmount,
        int TotalRequestsCount,
        IReadOnlyList<UserHistoryItemDto> History
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
        uint Version
    );

    [HttpGet]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<ActionResult<IEnumerable<object>>> GetUsers(CancellationToken ct)
    {
        var users = await _context.Users
            .OrderBy(u => u.FullName)
            .Select(u => new
            {
                u.Id,
                u.FullName,
                u.Email,
                u.PhoneNumber,
                u.SixDigitUserId,
                u.Role,
                u.Department,
                u.IsActive,
                u.CreatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(users);
    }

    /// <summary>
    /// Admin generates a new employee with a 6-digit ID and temporary password.
    /// Admin can copy credentials to WhatsApp/SMS.
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
            Email = request.Email.Trim().ToLowerInvariant(),
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
    /// Admin deep-dive history: Lifetime aggregate profile of employee.
    /// Total Approved (₹), Total Pending (₹), and all historical requests.
    /// </summary>
    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UserHistoryResponse>> GetUserHistory(
        Guid id,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user == null) return NotFound();

        var queryable = _context.PaymentRequests
            .Include(p => p.Site)
            .Include(p => p.Labourer)
            .Include(p => p.AuditLogs)
            .Where(p => p.UserId == id);

        if (startDate.HasValue)
        {
            queryable = queryable.Where(p => p.StartDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            queryable = queryable.Where(p => p.EndDate <= endDate.Value);
        }

        var payments = await queryable
            .OrderByDescending(p => p.RequestDate)
            .ToListAsync(ct);

        var totalApproved = payments.Where(p => p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed)
                                    .Sum(p => p.Amount);

        var totalPending = payments.Where(p => p.Status == PaymentStatus.Pending || p.Status == PaymentStatus.InfoRequested)
                                   .Sum(p => p.Amount);

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
                p.Version
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
            payments.Count,
            historyItems
        ));
    }
}

