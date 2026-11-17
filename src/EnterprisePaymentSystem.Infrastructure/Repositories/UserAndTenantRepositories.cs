using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly ApplicationDbContext _context;

    public UserRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var cleanInput = email.Trim().ToLower();
        var matches = await _context.Users
            .IgnoreQueryFilters()
            .Include(u => u.Tenant)
            .Where(u => (u.Email != null && u.Email.ToLower() == cleanInput) || u.SixDigitUserId == email.Trim())
            .Take(2)
            .ToListAsync(ct);

        // Identifiers must resolve to exactly one account across tenants. Never pick the first
        // match when a six-digit ID or email was duplicated in another tenant.
        return matches.Count == 1 ? matches[0] : null;
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
    }

    public async Task AddAsync(User user, CancellationToken ct = default)
    {
        await _context.Users.AddAsync(user, ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}

public class TenantRepository : ITenantRepository
{
    private readonly ApplicationDbContext _context;

    public TenantRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Tenants.FirstOrDefaultAsync(t => t.Id == id, ct);
    }

    public async Task<IReadOnlyList<Tenant>> GetAllActiveAsync(CancellationToken ct = default)
    {
        return await _context.Tenants
            .Where(t => t.IsActive)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Tenant tenant, CancellationToken ct = default)
    {
        await _context.Tenants.AddAsync(tenant, ct);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
