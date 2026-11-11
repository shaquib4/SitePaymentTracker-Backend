using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Helpers;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class AuditReportJobProcessor : IAuditReportJobProcessor
{
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
            var exportData = await _paymentRepository.GetAuditExportDataAsync(job.TenantId, filter, ct);

            var tenantName = await _context.Tenants
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(t => t.Id == job.TenantId)
                .Select(t => t.Name)
                .FirstOrDefaultAsync(ct) ?? job.TenantName;

            if (!string.IsNullOrWhiteSpace(exportData.SiteName) && string.IsNullOrWhiteSpace(entity.SiteName))
            {
                entity.SiteName = exportData.SiteName;
            }

            var paymentDtos = exportData.Items.Select(PaymentMapper.MapToDto).ToList();

            var timestampSlug = DateTime.UtcNow.ToString("yyyyMMdd_HHmm");
            var safePrefix = filter.SiteId.HasValue && !string.IsNullOrWhiteSpace(exportData.SiteName)
                ? $"Site_{SanitizeFileSegment(exportData.SiteName)}"
                : $"FY{filter.Year}";

            var pdfFileName = $"{safePrefix}_Breakdown_{timestampSlug}.pdf";
            var excelFileName = $"{safePrefix}_Expenses_{timestampSlug}.xlsx";

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
                TotalRecords: paymentDtos.Count,
                TotalRequestedAmount: exportData.TotalRequestedAmount,
                TotalApprovedAmount: exportData.TotalApprovedAmount,
                Summary: exportData.Summary,
                GeneratedAtUtc: DateTime.UtcNow
            ));

            var excelBytes = _excelService.GenerateAuditExpensesExcel(
                paymentDtos,
                tenantName,
                filter.Year,
                entity.PeriodLabel,
                entity.FilterSummary);

            entity.TotalRecords = paymentDtos.Count;
            entity.TotalRequestedAmount = exportData.TotalRequestedAmount;
            entity.TotalApprovedAmount = exportData.TotalApprovedAmount;
            entity.PdfFileName = pdfFileName;
            entity.ExcelFileName = excelFileName;
            entity.PdfBytes = pdfBytes;
            entity.ExcelBytes = excelBytes;
            entity.Status = AuditReportStatus.Completed;
            entity.CompletedAtUtc = DateTime.UtcNow;

            var apiBase = (job.BaseApiUrl ?? _configuration["Email:PublicApiBaseUrl"] ?? "").TrimEnd('/');
            var frontendBase = (job.FrontendUrl ?? _configuration["Email:FrontendUrl"] ?? "https://khan-enterprises-payment-tracker.onrender.com").TrimEnd('/');
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
        return cleaned.Trim('_').Length > 32 ? cleaned.Trim('_')[..32] : cleaned.Trim('_');
    }
}
