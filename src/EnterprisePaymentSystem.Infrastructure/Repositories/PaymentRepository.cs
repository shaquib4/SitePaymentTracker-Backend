using EnterprisePaymentSystem.Application.DTOs;
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

    public async Task<PagedPaymentEntitiesResult> GetPagedPaymentsAsync(
        PaymentFilterQuery query,
        Guid? userIdFilter = null,
        CancellationToken ct = default)
    {
        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(query.PageSize, 1, 200);

        var baseQuery = _context.PaymentRequests
            .AsNoTracking()
            .Where(p => p.ExpenseYear == query.Year);

        if (userIdFilter.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.UserId == userIdFilter.Value);
        }

        if (query.SiteId.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.SiteId == query.SiteId.Value);
        }

        if (query.StartDate.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.StartDate >= query.StartDate.Value);
        }

        if (query.EndDate.HasValue)
        {
            baseQuery = baseQuery.Where(p => p.EndDate <= query.EndDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Scope))
        {
            var scope = query.Scope.Trim().ToLower();
            if (scope == "wage_requests")
            {
                baseQuery = baseQuery.Where(p => p.LabourerId != null);
            }
            else if (scope == "material_requests")
            {
                baseQuery = baseQuery.Where(p => p.LabourerId == null);
            }
            else if (scope == "my_wages")
            {
                baseQuery = baseQuery.Where(p =>
                    p.LabourerId == null &&
                    (p.Category.Contains("Wage") || p.Category.Contains("Salary") || p.Category.Contains("Supervisor")));
            }
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var s = query.Search.Trim().ToLower();
            baseQuery = baseQuery.Where(p =>
                (p.Title != null && p.Title.ToLower().Contains(s)) ||
                (p.Description != null && p.Description.ToLower().Contains(s)) ||
                (p.Category != null && p.Category.ToLower().Contains(s)) ||
                (p.InvoiceNumber != null && p.InvoiceNumber.ToLower().Contains(s)) ||
                (p.User != null && p.User.FullName != null && p.User.FullName.ToLower().Contains(s)) ||
                (p.User != null && p.User.PhoneNumber != null && p.User.PhoneNumber.ToLower().Contains(s)) ||
                (p.Site != null && p.Site.Name != null && p.Site.Name.ToLower().Contains(s)) ||
                (p.Labourer != null && p.Labourer.Name != null && p.Labourer.Name.ToLower().Contains(s)));
        }

        var summaryQuery = baseQuery;
        if (query.Status.HasValue)
        {
            summaryQuery = summaryQuery.Where(p => p.Status == query.Status.Value);
        }

        // Database-level status aggregates across all matching rows (even 100k+)
        var statusGroups = await summaryQuery
            .GroupBy(p => p.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Amount = g.Sum(p => p.Amount)
            })
            .ToListAsync(ct);

        decimal approvedAmt = 0m, completedAmt = 0m, pendingAmt = 0m, infoAmt = 0m, rejectedAmt = 0m;
        int approvedCount = 0, completedCount = 0, pendingCount = 0, infoCount = 0, rejectedCount = 0;

        foreach (var g in statusGroups)
        {
            switch (g.Status)
            {
                case PaymentStatus.Approved:
                    approvedAmt = g.Amount;
                    approvedCount = g.Count;
                    break;
                case PaymentStatus.Completed:
                    completedAmt = g.Amount;
                    completedCount = g.Count;
                    break;
                case PaymentStatus.Pending:
                    pendingAmt = g.Amount;
                    pendingCount = g.Count;
                    break;
                case PaymentStatus.InfoRequested:
                    infoAmt = g.Amount;
                    infoCount = g.Count;
                    break;
                case PaymentStatus.Rejected:
                    rejectedAmt = g.Amount;
                    rejectedCount = g.Count;
                    break;
            }
        }

        var grandTotalAmount = approvedAmt + completedAmt + pendingAmt + infoAmt + rejectedAmt;

        // Pie chart category breakdowns reflect active status filter if specified;
        // otherwise prioritize Approved + Completed and fall back to all statuses if none.
        var pieQuery = summaryQuery;
        if (!query.Status.HasValue && (approvedCount + completedCount) > 0)
        {
            pieQuery = pieQuery.Where(p =>
                p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed);
        }

        var rawCategoryGroups = await pieQuery
            .GroupBy(p => p.Category)
            .Select(g => new
            {
                Category = g.Key,
                Count = g.Count(),
                TotalAmount = g.Sum(p => p.Amount)
            })
            .OrderByDescending(c => c.TotalAmount)
            .ToListAsync(ct);

        var categoryBreakdowns = rawCategoryGroups
            .Select(c => new CategoryBreakdownDto(
                string.IsNullOrWhiteSpace(c.Category) ? "General" : c.Category,
                c.Count,
                c.TotalAmount))
            .ToList();

        var summary = new PaymentPagedSummaryDto(
            GrandTotalAmount: grandTotalAmount,
            TotalApprovedAmount: approvedAmt,
            TotalCompletedAmount: completedAmt,
            TotalPendingAmount: pendingAmt,
            TotalInfoRequestedAmount: infoAmt,
            TotalRejectedAmount: rejectedAmt,
            TotalApprovedCount: approvedCount,
            TotalCompletedCount: completedCount,
            TotalPendingCount: pendingCount,
            TotalInfoRequestedCount: infoCount,
            TotalRejectedCount: rejectedCount,
            CategoryBreakdowns: categoryBreakdowns
        );

        var pagedQuery = summaryQuery;
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            var cat = query.Category.Trim();
            pagedQuery = pagedQuery.Where(p => p.Category == cat);
        }

        var totalCount = await pagedQuery.CountAsync(ct);
        var totalPages = totalCount == 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);

        var items = await pagedQuery
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Include(p => p.User)
            .Include(p => p.Site)
            .Include(p => p.Labourer)
            .Include(p => p.AuditLogs)
            .ToListAsync(ct);

        return new PagedPaymentEntitiesResult(
            Items: items,
            Page: page,
            PageSize: pageSize,
            TotalCount: totalCount,
            TotalPages: totalPages,
            Summary: summary
        );
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
