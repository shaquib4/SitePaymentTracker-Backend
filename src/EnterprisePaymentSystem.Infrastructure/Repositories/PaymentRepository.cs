using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using EnterprisePaymentSystem.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Infrastructure.Repositories;

public class PaymentRepository : IPaymentRepository
{
    private readonly ApplicationDbContext _context;

    public PaymentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PaymentRequest?> GetByIdWithAuditLogsAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.PaymentRequests
            .Include(p => p.User)
            .Include(p => p.AuditLogs)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<PaymentRequest>> GetFilteredPaymentsAsync(
        int year,
        PaymentStatus? status = null,
        string? category = null,
        string? search = null,
        Guid? userIdFilter = null,
        CancellationToken ct = default)
    {
        var queryable = _context.PaymentRequests
            .Include(p => p.User)
            .Include(p => p.AuditLogs)
            .Where(p => p.ExpenseYear == year);

        if (userIdFilter.HasValue)
        {
            queryable = queryable.Where(p => p.UserId == userIdFilter.Value);
        }

        if (status.HasValue)
        {
            queryable = queryable.Where(p => p.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            queryable = queryable.Where(p => p.Category == category.Trim());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            queryable = queryable.Where(p =>
                p.Title.ToLower().Contains(s) ||
                p.Description.ToLower().Contains(s) ||
                (p.InvoiceNumber != null && p.InvoiceNumber.ToLower().Contains(s)) ||
                (p.User != null && p.User.FullName.ToLower().Contains(s)));
        }

        return await queryable
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentRequest>> GetPaymentsByYearAsync(int year, CancellationToken ct = default)
    {
        return await _context.PaymentRequests
            .Where(p => p.ExpenseYear == year)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<PaymentRequest>> GetPaymentsForExportAsync(Guid tenantId, int year, CancellationToken ct = default)
    {
        // Background worker can query directly across tenant filter by matching specific tenant ID
        return await _context.PaymentRequests
            .IgnoreQueryFilters()
            .Include(p => p.User)
            .Include(p => p.AuditLogs)
            .Where(p => p.TenantId == tenantId && p.ExpenseYear == year)
            .OrderByDescending(p => p.CreatedAtUtc)
            .ToListAsync(ct);
    }

    public async Task AddAsync(PaymentRequest payment, CancellationToken ct = default)
    {
        await _context.PaymentRequests.AddAsync(payment, ct);
    }

    public Task UpdateAsync(PaymentRequest payment, CancellationToken ct = default)
    {
        if (_context.Entry(payment).State == EntityState.Detached)
        {
            _context.PaymentRequests.Attach(payment);
            _context.Entry(payment).State = EntityState.Modified;
        }
        return Task.CompletedTask;
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        return await _context.SaveChangesAsync(ct);
    }
}
