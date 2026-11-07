using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly IApplicationDbContext _context;
    private readonly ICurrentUserService _currentUserService;

    public CategoriesController(IApplicationDbContext context, ICurrentUserService currentUserService)
    {
        _context = context;
        _currentUserService = currentUserService;
    }

    public record CreateCategoryRequest(string Name);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ExpenseCategory>>> GetCategories(CancellationToken ct)
    {
        var list = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .ToListAsync(ct);

        return Ok(list);
    }

    /// <summary>
    /// Admins can dynamically add new categories (e.g. "Sariya", "Cement", "Sand").
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ExpenseCategory>> CreateCategory([FromBody] CreateCategoryRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Category name cannot be empty.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var tenantId = _currentUserService.TenantId ?? throw new UnauthorizedAccessException();
        var normalizedName = request.Name.Trim();

        var existing = await _context.Categories
            .FirstOrDefaultAsync(c => c.Name.ToLower() == normalizedName.ToLower(), ct);

        if (existing != null)
        {
            return Ok(existing);
        }

        var category = new ExpenseCategory
        {
            TenantId = tenantId,
            Name = normalizedName,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _context.Categories.Add(category);
        await _context.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(GetCategories), new { id = category.Id }, category);
    }
}

