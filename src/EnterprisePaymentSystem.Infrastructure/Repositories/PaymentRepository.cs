using System.Runtime.CompilerServices;
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

        var baseQuery = ApplyCommonFilters(
            _context.PaymentRequests.AsNoTracking(),
            query.Year,
            query.SiteId,
            query.StartDate,
            query.EndDate,
            query.Search,
            userIdFilter,
            query.Scope);

        var summaryQuery = query.Status.HasValue
            ? baseQuery.Where(p => p.Status == query.Status.Value)
            : baseQuery;

        var summary = await ComputeSummaryAsync(summaryQuery, query.Status, ct);

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

    private IQueryable<PaymentRequest> BuildTenantAuditQuery(Guid tenantId, AuditExportFilter filter)
    {
        var tenantScoped = _context.PaymentRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId);

        var filtered = ApplyCommonFilters(
            tenantScoped,
            filter.Year,
            filter.SiteId,
            filter.StartDate,
            filter.EndDate,
            filter.Search,
            userIdFilter: filter.EmployeeId,
            scope: null);

        if (filter.Status.HasValue)
        {
            filtered = filtered.Where(p => p.Status == filter.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.Category))
        {
            var cat = filter.Category.Trim();
            filtered = filtered.Where(p => p.Category == cat);
        }

        return filtered;
    }

    public async Task<AuditExportSummaryResult> GetAuditExportSummaryAsync(
        Guid tenantId,
        AuditExportFilter filter,
        CancellationToken ct = default)
    {
        var filtered = BuildTenantAuditQuery(tenantId, filter);
        var summary = await ComputeSummaryAsync(filtered, filter.Status, ct);

        // Single database aggregation for TotalRecords, TotalRequestedAmount, and TotalApprovedAmount
        var totalsByStatus = await filtered
            .GroupBy(p => p.Status)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count(),
                Requested = g.Sum(p => p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount),
                Amount = g.Sum(p => p.Amount)
            })
            .ToListAsync(ct);

        int totalRecords = totalsByStatus.Sum(x => x.Count);
        decimal totalRequestedAmount = totalsByStatus.Sum(x => x.Requested);
        decimal totalApprovedAmount = totalsByStatus
            .Where(x => x.Status == PaymentStatus.Approved || x.Status == PaymentStatus.Completed)
            .Sum(x => x.Amount);

        string? siteName = null;
        if (filter.SiteId.HasValue)
        {
            siteName = await _context.Sites
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(s => s.TenantId == tenantId && s.Id == filter.SiteId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
        }

        return new AuditExportSummaryResult(
            TotalRecords: totalRecords,
            TotalRequestedAmount: totalRequestedAmount,
            TotalApprovedAmount: totalApprovedAmount,
            Summary: summary,
            SiteName: siteName
        );
    }

    public async IAsyncEnumerable<IReadOnlyList<AuditExpenseRowDto>> StreamAuditExpenseChunksAsync(
        Guid tenantId,
        AuditExportFilter filter,
        int chunkSize = 2000,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var effectiveChunkSize = Math.Clamp(chunkSize, 1, 5000);
        var baseFiltered = BuildTenantAuditQuery(tenantId, filter);

        DateTime? lastCreatedAt = null;
        var seenIdsAtCursor = new List<Guid>();

        while (!ct.IsCancellationRequested)
        {
            var chunkQuery = baseFiltered;
            if (lastCreatedAt.HasValue)
            {
                var cursorTime = lastCreatedAt.Value;
                var excluded = seenIdsAtCursor;
                chunkQuery = chunkQuery.Where(p =>
                    p.CreatedAtUtc < cursorTime ||
                    (p.CreatedAtUtc == cursorTime && !excluded.Contains(p.Id)));
            }

            var rawChunk = await chunkQuery
                .OrderByDescending(p => p.CreatedAtUtc)
                .ThenByDescending(p => p.Id)
                .Take(effectiveChunkSize)
                .Select(p => new
                {
                    p.Id,
                    SubmitterName = p.User != null ? p.User.FullName : "Unknown",
                    SubmitterEmail = p.User != null && p.User.Email != null ? p.User.Email : "Unknown",
                    SiteName = p.Site != null ? p.Site.Name : "Head Office / General",
                    p.Title,
                    p.Description,
                    p.Category,
                    p.RequestedAmount,
                    p.Amount,
                    p.Status,
                    p.Currency,
                    p.StartDate,
                    p.EndDate,
                    p.InvoiceNumber,
                    p.CreatedAtUtc
                })
                .ToListAsync(ct);

            if (rawChunk.Count == 0)
            {
                yield break;
            }

            var mappedChunk = rawChunk.Select(p => new AuditExpenseRowDto(
                Id: p.Id,
                SubmitterName: p.SubmitterName,
                SubmitterEmail: p.SubmitterEmail,
                SiteName: string.IsNullOrWhiteSpace(p.SiteName) ? "Head Office / General" : p.SiteName,
                Title: p.Title,
                Description: string.IsNullOrWhiteSpace(p.Description) ? "-" : p.Description,
                Category: p.Category,
                RequestedAmount: p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount,
                ApprovedAmount: p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed
                    ? p.Amount
                    : (decimal?)null,
                Currency: p.Currency,
                StartDate: p.StartDate,
                EndDate: p.EndDate,
                StatusName: p.Status.ToString(),
                InvoiceNumber: string.IsNullOrWhiteSpace(p.InvoiceNumber) ? "-" : p.InvoiceNumber
            )).ToList();

            yield return mappedChunk;

            if (rawChunk.Count < effectiveChunkSize)
            {
                yield break;
            }

            var nextCursorTime = rawChunk[^1].CreatedAtUtc;
            var idsAtTime = rawChunk
                .Where(x => x.CreatedAtUtc == nextCursorTime)
                .Select(x => x.Id)
                .ToList();

            if (lastCreatedAt.HasValue && nextCursorTime == lastCreatedAt.Value)
            {
                seenIdsAtCursor.AddRange(idsAtTime);
            }
            else
            {
                lastCreatedAt = nextCursorTime;
                seenIdsAtCursor = idsAtTime;
            }
        }
    }

    public async Task<AuditExportDataResult> GetAuditExportDataAsync(
        Guid tenantId,
        AuditExportFilter filter,
        CancellationToken ct = default)
    {
        var summaryResult = await GetAuditExportSummaryAsync(tenantId, filter, ct);
        var filtered = BuildTenantAuditQuery(tenantId, filter);

        var items = await filtered
            .OrderByDescending(p => p.StartDate)
            .ThenByDescending(p => p.CreatedAtUtc)
            .Include(p => p.User)
            .Include(p => p.Site)
            .Include(p => p.Labourer)
            .ToListAsync(ct);

        return new AuditExportDataResult(
            Items: items,
            Summary: summaryResult.Summary,
            TotalRequestedAmount: summaryResult.TotalRequestedAmount,
            TotalApprovedAmount: summaryResult.TotalApprovedAmount,
            SiteName: summaryResult.SiteName
        );
    }

    private static IQueryable<PaymentRequest> ApplyCommonFilters(
        IQueryable<PaymentRequest> source,
        int year,
        Guid? siteId,
        DateOnly? startDate,
        DateOnly? endDate,
        string? search,
        Guid? userIdFilter,
        string? scope)
    {
        var query = source.Where(p => p.ExpenseYear == year);

        if (userIdFilter.HasValue)
        {
            query = query.Where(p => p.UserId == userIdFilter.Value);
        }

        if (siteId.HasValue)
        {
            query = query.Where(p => p.SiteId == siteId.Value);
        }

        if (startDate.HasValue)
        {
            query = query.Where(p => p.StartDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(p => p.EndDate <= endDate.Value);
        }

        if (!string.IsNullOrWhiteSpace(scope))
        {
            var normalizedScope = scope.Trim().ToLower();
            if (normalizedScope == "wage_requests")
            {
                query = query.Where(p => p.LabourerId != null);
            }
            else if (normalizedScope == "material_requests")
            {
                query = query.Where(p => p.LabourerId == null);
            }
            else if (normalizedScope == "my_wages")
            {
                query = query.Where(p =>
                    p.LabourerId == null &&
                    (p.Category.Contains("Wage") || p.Category.Contains("Salary") || p.Category.Contains("Supervisor")));
            }
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            query = query.Where(p =>
                (p.Title != null && p.Title.ToLower().Contains(s)) ||
                (p.Description != null && p.Description.ToLower().Contains(s)) ||
                (p.Category != null && p.Category.ToLower().Contains(s)) ||
                (p.InvoiceNumber != null && p.InvoiceNumber.ToLower().Contains(s)) ||
                (p.User != null && p.User.FullName != null && p.User.FullName.ToLower().Contains(s)) ||
                (p.User != null && p.User.PhoneNumber != null && p.User.PhoneNumber.ToLower().Contains(s)) ||
                (p.Site != null && p.Site.Name != null && p.Site.Name.ToLower().Contains(s)) ||
                (p.Labourer != null && p.Labourer.Name != null && p.Labourer.Name.ToLower().Contains(s)));
        }

        return query;
    }

    private static async Task<PaymentPagedSummaryDto> ComputeSummaryAsync(
        IQueryable<PaymentRequest> summaryQuery,
        PaymentStatus? statusFilter,
        CancellationToken ct)
    {
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

        var pieQuery = summaryQuery;
        if (!statusFilter.HasValue && (approvedCount + completedCount) > 0)
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

        return new PaymentPagedSummaryDto(
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
