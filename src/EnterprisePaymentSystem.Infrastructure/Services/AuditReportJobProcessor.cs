using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class AuditReportJobProcessor : IAuditReportJobProcessor
{
    private const int ExportChunkSize = 2000;

    private readonly IApplicationDbContext _context;
    private readonly IPaymentRepository _paymentRepository;
    private readonly IPdfBreakdownExportService _pdfService;
    private readonly IExcelExportService _excelService;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuditReportJobProcessor> _logger;

    public AuditReportJobProcessor(
        IApplicationDbContext context,
        IPaymentRepository paymentRepository,
        IPdfBreakdownExportService pdfService,
        IExcelExportService excelService,
        IEmailService emailService,
        IConfiguration configuration,
        ILogger<AuditReportJobProcessor> logger)
    {
        _context = context;
        _paymentRepository = paymentRepository;
        _pdfService = pdfService;
        _excelService = excelService;
        _emailService = emailService;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task ProcessAuditPackJobAsync(ExportJobRequest job, CancellationToken ct = default)
    {
        var auditJobId = job.AuditReportJobId ?? Guid.Empty;
        var entity = await _context.AuditReportJobs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == auditJobId && j.TenantId == job.TenantId, ct);

        if (entity == null)
        {
            _logger.LogWarning("AuditReportJob {AuditJobId} not found in database.", auditJobId);
            return;
        }

        try
        {
            entity.Status = AuditReportStatus.Processing;
            await _context.SaveChangesAsync(ct);

            var filter = job.AuditFilter ?? new AuditExportFilter(job.Year);

            // 1. SQL-only aggregation (handles 100,000+ rows with zero itemized entity load)
            var exportSummary = await _paymentRepository.GetAuditExportSummaryAsync(job.TenantId, filter, ct);

            var tenantName = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.Id == job.TenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct) ?? job.TenantName;

            if (!string.IsNullOrWhiteSpace(exportSummary.SiteName) && string.IsNullOrWhiteSpace(entity.SiteName))
            {
                entity.SiteName = exportSummary.SiteName;
            }

            var timestampSlug = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
            var safePrefix = filter.SiteId.HasValue && !string.IsNullOrWhiteSpace(exportSummary.SiteName)
                ? $"Site_{SanitizeFileSegment(exportSummary.SiteName)}"
                : $"FY{filter.Year}";

            var pdfFileName = $"{safePrefix}_Breakdown_{timestampSlug}.pdf";
            var excelFileName = $"{safePrefix}_Expenses_{timestampSlug}.xlsx";

            // 2. Generate executive Breakdown-only PDF from SQL summary
            var pdfBytes = _pdfService.GenerateBreakdownPdf(new PdfBreakdownContext(
                JobReference: entity.Id.ToString("N")[..8].ToUpperInvariant(),
                TenantName: tenantName,
                ReportTitle: entity.ReportTitle,
                PeriodLabel: entity.PeriodLabel,
                FilterSummary: entity.FilterSummary,
                PreparedByName: entity.RequestedByName,
                PreparedByEmail: entity.RequestedByEmail,
                SiteName: entity.SiteName,
                StatusFilter: entity.StatusFilter,
                CategoryFilter: entity.CategoryFilter,
                ExcelFileName: excelFileName,
                TotalRecords: exportSummary.TotalRecords,
                TotalRequestedAmount: exportSummary.TotalRequestedAmount,
                TotalApprovedAmount: exportSummary.TotalApprovedAmount,
                Summary: exportSummary.Summary,
                GeneratedAtUtc: DateTime.UtcNow
            ));

            // 3. Stream matching rows in keyset chunks (2,000 rows/chunk) directly into SAX OpenXML .xlsx
            var rowChunks = _paymentRepository.StreamAuditExpenseChunksAsync(
                job.TenantId,
                filter,
                ExportChunkSize,
                ct);

            var excelBytes = await _excelService.GenerateChunkedAuditExpensesExcelAsync(
                rowChunks,
                tenantName,
                filter.Year,
                entity.PeriodLabel,
                entity.FilterSummary,
                ct);

            entity.TotalRecords = exportSummary.TotalRecords;
            entity.TotalRequestedAmount = exportSummary.TotalRequestedAmount;
            entity.TotalApprovedAmount = exportSummary.TotalApprovedAmount;
            entity.PdfFileName = pdfFileName;
            entity.ExcelFileName = excelFileName;
            entity.PdfBytes = pdfBytes;
            entity.ExcelBytes = excelBytes;
            entity.PdfDownloadedAtUtc = null;
            entity.ExcelDownloadedAtUtc = null;
            entity.Status = AuditReportStatus.Completed;
            entity.CompletedAtUtc = DateTime.UtcNow;

            var apiBase = (job.BaseApiUrl ?? _configuration["Email:PublicApiBaseUrl"] ?? _configuration["PUBLIC_API_BASE_URL"] ?? "").TrimEnd('/');
            var frontendBase = (job.FrontendUrl ?? _configuration["Email:FrontendUrl"] ?? _configuration["FRONTEND_URL"] ?? "https://site-payment-tracker-frontend.vercel.app").TrimEnd('/');
            var tokenQuery = Uri.EscapeDataString(entity.DownloadToken);

            var pdfUrl = $"{apiBase}/api/reports/admin-files/{entity.Id}/download/pdf?token={tokenQuery}";
            var excelUrl = $"{apiBase}/api/reports/admin-files/{entity.Id}/download/xlsx?token={tokenQuery}";

            var emailResult = await _emailService.SendAuditReportReadyEmailAsync(
                new AuditEmailNotification(
                    RecipientEmail: entity.RequestedByEmail,
                    RecipientName: entity.RequestedByName,
                    TenantName: tenantName,
                    ReportTitle: entity.ReportTitle,
                    PeriodLabel: entity.PeriodLabel,
                    FilterSummary: entity.FilterSummary,
                    TotalRecords: entity.TotalRecords,
                    TotalRequestedAmount: entity.TotalRequestedAmount,
                    TotalApprovedAmount: entity.TotalApprovedAmount,
                    PdfDownloadUrl: pdfUrl,
                    ExcelDownloadUrl: excelUrl,
                    AuditFilesPortalUrl: frontendBase
                ),
                ct);

            entity.EmailDispatched = emailResult.Dispatched;
            entity.EmailDeliveryNote = emailResult.DeliveryNote;

            await _context.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Audit Pack Job {AuditJobId} failed.", auditJobId);
            entity.Status = AuditReportStatus.Failed;
            entity.ErrorMessage = ex.Message.Length > 950 ? ex.Message[..950] : ex.Message;
            entity.CompletedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static string SanitizeFileSegment(string value)
    {
        var chars = value.Trim()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        var cleaned = new string(chars);
        while (cleaned.Contains("__", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("__", "_", StringComparison.Ordinal);
        }
        var trimmed = cleaned.Trim('_');
        return trimmed.Length > 32 ? trimmed[..32] : trimmed;
    }
}
