using System.Security.Cryptography;
using System.Text;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IBackgroundExportQueue _queue;
    private readonly ICurrentUserService _currentUserService;
    private readonly IApplicationDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;
    private readonly IValidator<ExportReportRequest> _validator;
    private readonly IValidator<AdminAuditPackRequest> _adminAuditValidator;
    private readonly string _storageDirectory;

    public ReportsController(
        IBackgroundExportQueue queue,
        ICurrentUserService currentUserService,
        IApplicationDbContext context,
        IEmailService emailService,
        IConfiguration configuration,
        IValidator<ExportReportRequest> validator,
        IValidator<AdminAuditPackRequest> adminAuditValidator)
    {
        _queue = queue;
        _currentUserService = currentUserService;
        _context = context;
        _emailService = emailService;
        _configuration = configuration;
        _validator = validator;
        _adminAuditValidator = adminAuditValidator;
        var defaultDir = Path.Combine(AppContext.BaseDirectory, "Storage", "Reports");
        _storageDirectory = Directory.Exists(defaultDir)
            ? defaultDir
            : Path.Combine(Path.GetTempPath(), "Storage", "Reports");
    }

    /// <summary>
    /// Drops export job in background queue, decoupling HTTP request from heavy Excel generation. Returns 202 Accepted.
    /// </summary>
    [HttpPost("export-async")]
    [Authorize(Roles = "FinanceAdmin,TenantAdmin,Admin")]
    public async Task<ActionResult<ExportJobStatusResponse>> ExportAsync(
        [FromBody] ExportReportRequest request,
        CancellationToken ct)
    {
        var validationResult = await _validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var tenantId = _currentUserService.TenantId ?? Guid.Empty;
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var email = _currentUserService.Email ?? "finance@organization.com";

        var jobId = Guid.NewGuid().ToString("N");
        var jobRequest = new ExportJobRequest(
            JobId: jobId,
            TenantId: tenantId,
            TenantName: "Tenant Expenses",
            Year: request.Year,
            RequestedByUserId: userId,
            RequestedByEmail: email,
            QueuedAtUtc: DateTime.UtcNow
        );

        await _queue.QueueExportJobAsync(jobRequest, ct);

        var status = _queue.GetJobStatus(jobId);
        return Accepted($"/api/reports/status/{jobId}", status);
    }

    /// <summary>
    /// Checks status of the queued Excel generation job.
    /// </summary>
    [HttpGet("status/{jobId}")]
    [Authorize(Roles = "FinanceAdmin,TenantAdmin,Admin")]
    public ActionResult<ExportJobStatusResponse> GetStatus(string jobId)
    {
        var status = _queue.GetJobStatus(jobId);
        if (status == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Job not found",
                Detail = $"Export job '{jobId}' does not exist.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(status);
    }

    /// <summary>
    /// Streams generated Excel report file.
    /// </summary>
    [HttpGet("download/{jobId}")]
    [AllowAnonymous]
    public IActionResult DownloadReport(string jobId)
    {
        var filePath = Path.Combine(_storageDirectory, $"{jobId}.xlsx");
        if (!System.IO.File.Exists(filePath))
        {
            return NotFound(new ProblemDetails
            {
                Title = "Report not found",
                Detail = "The requested export file was not found or is still generating.",
                Status = StatusCodes.Status404NotFound
            });
        }

        var fileStream = System.IO.File.OpenRead(filePath);
        return File(
            fileStream,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"ExpenseReport_{jobId[..Math.Min(8, jobId.Length)]}.xlsx"
        );
    }

    /// <summary>
    /// Admin-only diagnostic endpoint checking whether SMTP or Resend mail credentials are active on the server.
    /// </summary>
    [HttpGet("email-status")]
    [Authorize(Roles = "Admin")]
    public ActionResult<EmailConfigurationStatusDto> GetEmailConfigurationStatus()
    {
        return Ok(_emailService.GetConfigurationStatus());
    }

    /// <summary>
    /// Strictly Admin-only: Queues a background job that generates a formal Breakdown PDF
    /// AND a streamed Expenses Excel ledger of all matching expenses for the selected period &amp; filters,
    /// saves them temporarily until downloaded, and dispatches a notification email with download links.
    /// </summary>
    [HttpPost("admin-audit-pack")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AuditReportJobDto>> QueueAdminAuditPack(
        [FromBody] AdminAuditPackRequest request,
        CancellationToken ct)
    {
        var validationResult = await _adminAuditValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var tenantId = _currentUserService.TenantId ?? Guid.Empty;
        var userId = _currentUserService.UserId ?? Guid.Empty;
        var adminName = _currentUserService.FullName ?? "Administrator";

        var userEmail = _currentUserService.Email;
        if (string.IsNullOrWhiteSpace(userEmail) && userId != Guid.Empty)
        {
            userEmail = await _context.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => u.Email)
                .FirstOrDefaultAsync(ct);
        }

        var recipientEmail = !string.IsNullOrWhiteSpace(request.RecipientEmail)
            ? request.RecipientEmail.Trim()
            : (!string.IsNullOrWhiteSpace(userEmail) ? userEmail.Trim() : "admin@khanenterprises.com");

        var tenantName = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? "Khan Enterprises";

        string? siteName = null;
        if (request.SiteId.HasValue)
        {
            siteName = await _context.Sites
                .AsNoTracking()
                .Where(s => s.Id == request.SiteId.Value)
                .Select(s => s.Name)
                .FirstOrDefaultAsync(ct);
        }

        var periodLabel = BuildPeriodLabel(request.Year, request.StartDate, request.EndDate);
        var filterSummary = BuildFilterSummary(request, siteName);
        var reportTitle = string.IsNullOrWhiteSpace(request.ReportTitle)
            ? (!string.IsNullOrWhiteSpace(siteName)
                ? $"{siteName} Audit Pack ({periodLabel})"
                : $"Expense Breakdown & Ledger Audit ({periodLabel})")
            : request.ReportTitle.Trim();

        var auditJob = new AuditReportJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RequestedByUserId = userId,
            RequestedByName = adminName,
            RequestedByEmail = recipientEmail,
            ReportTitle = reportTitle,
            PeriodLabel = periodLabel,
            FilterSummary = filterSummary,
            Year = request.Year,
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            StatusFilter = request.Status?.ToString(),
            CategoryFilter = string.IsNullOrWhiteSpace(request.Category) ? null : request.Category.Trim(),
            SiteId = request.SiteId,
            SiteName = siteName,
            SearchFilter = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim(),
            Status = AuditReportStatus.Queued,
            DownloadToken = GenerateSecureToken(),
            QueuedAtUtc = DateTime.UtcNow
        };

        await _context.AuditReportJobs.AddAsync(auditJob, ct);
        await _context.SaveChangesAsync(ct);

        await EnqueueAuditJobAsync(auditJob, tenantName, request.Status, ct);

        return Accepted($"/api/reports/admin-files/{auditJob.Id}", ToDto(auditJob));
    }

    /// <summary>
    /// Strictly Admin-only: Regenerates PDF + Excel using the preserved date range and filter metadata
    /// on an existing Audit Files entry (for example after files were downloaded and purged from DB).
    /// </summary>
    [HttpPost("admin-files/{jobId:guid}/regenerate")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AuditReportJobDto>> RegenerateAdminAuditPack(
        Guid jobId,
        [FromBody] RegenerateAuditPackRequest? request = null,
        CancellationToken ct = default)
    {
        var auditJob = await _context.AuditReportJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (auditJob == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Audit entry not found",
                Detail = "The requested audit report record was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (auditJob.Status == AuditReportStatus.Queued || auditJob.Status == AuditReportStatus.Processing)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Already generating",
                Detail = "This audit pack is currently being generated.",
                Status = StatusCodes.Status409Conflict
            });
        }

        PaymentStatus? parsedStatus = null;
        if (!string.IsNullOrWhiteSpace(auditJob.StatusFilter) &&
            Enum.TryParse<PaymentStatus>(auditJob.StatusFilter, ignoreCase: true, out var statusVal))
        {
            parsedStatus = statusVal;
        }

        if (!string.IsNullOrWhiteSpace(request?.RecipientEmail))
        {
            auditJob.RequestedByEmail = request.RecipientEmail.Trim();
        }

        auditJob.Status = AuditReportStatus.Queued;
        auditJob.PdfBytes = null;
        auditJob.ExcelBytes = null;
        auditJob.PdfDownloadedAtUtc = null;
        auditJob.ExcelDownloadedAtUtc = null;
        auditJob.CompletedAtUtc = null;
        auditJob.ErrorMessage = null;
        auditJob.EmailDispatched = false;
        auditJob.EmailDeliveryNote = null;
        auditJob.DownloadToken = GenerateSecureToken();
        auditJob.QueuedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync(ct);

        var tenantName = await _context.Tenants
            .AsNoTracking()
            .Where(t => t.Id == auditJob.TenantId)
            .Select(t => t.Name)
            .FirstOrDefaultAsync(ct) ?? "Khan Enterprises";

        await EnqueueAuditJobAsync(auditJob, tenantName, parsedStatus, ct);

        return Accepted($"/api/reports/admin-files/{auditJob.Id}", ToDto(auditJob));
    }

    /// <summary>
    /// Strictly Admin-only: Lists Audit Pack records (preserves filename, period, and filters even after binaries are purged post-download).
    /// </summary>
    [HttpGet("admin-files")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<IReadOnlyList<AuditReportJobDto>>> ListAdminAuditFiles(CancellationToken ct)
    {
        var items = await _context.AuditReportJobs
            .AsNoTracking()
            .OrderByDescending(j => j.QueuedAtUtc)
            .Take(50)
            .Select(j => new AuditReportJobDto(
                j.Id,
                j.ReportTitle,
                j.PeriodLabel,
                j.FilterSummary,
                j.Year,
                j.StartDate,
                j.EndDate,
                j.StatusFilter,
                j.CategoryFilter,
                j.SiteId,
                j.SiteName,
                j.SearchFilter,
                (ExportJobStatus)(int)j.Status,
                j.Status.ToString(),
                j.TotalRecords,
                j.TotalRequestedAmount,
                j.TotalApprovedAmount,
                j.PdfFileName,
                j.ExcelFileName,
                j.Status == AuditReportStatus.Completed && j.PdfBytes != null,
                j.Status == AuditReportStatus.Completed && j.ExcelBytes != null,
                j.PdfDownloadedAtUtc,
                j.ExcelDownloadedAtUtc,
                j.RequestedByName,
                j.RequestedByEmail,
                j.EmailDispatched,
                j.EmailDeliveryNote,
                j.QueuedAtUtc,
                j.CompletedAtUtc,
                j.ErrorMessage
            ))
            .ToListAsync(ct);

        return Ok(items);
    }

    /// <summary>
    /// Streams Breakdown PDF ('pdf') or Expenses Excel ('xlsx') and deletes its heavy bytea payload
    /// from the database immediately after serving while preserving filename, date range, and filter metadata for regeneration.
    /// </summary>
    [HttpGet("admin-files/{jobId:guid}/download/{format}")]
    [AllowAnonymous]
    public async Task<IActionResult> DownloadAdminAuditFile(
        Guid jobId,
        string format,
        [FromQuery] string? token = null,
        CancellationToken ct = default)
    {
        var normalizedFormat = format.Trim().ToLowerInvariant();
        if (normalizedFormat != "pdf" && normalizedFormat != "xlsx")
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid format",
                Detail = "Supported formats are 'pdf' and 'xlsx'.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var job = await _context.AuditReportJobs
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Audit file not found",
                Detail = "The requested audit report job was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            var expectedBytes = Encoding.UTF8.GetBytes(job.DownloadToken);
            var suppliedBytes = Encoding.UTF8.GetBytes(token.Trim());
            if (expectedBytes.Length == 0 ||
                expectedBytes.Length != suppliedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(expectedBytes, suppliedBytes))
            {
                return Forbid();
            }
        }
        else
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return Unauthorized();
            }
            if (!User.IsInRole("Admin") || _currentUserService.TenantId != job.TenantId)
            {
                return Forbid();
            }
        }

        if (job.Status != AuditReportStatus.Completed)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Report still generating",
                Detail = $"Current job status: {job.Status}.",
                Status = StatusCodes.Status409Conflict
            });
        }

        if (normalizedFormat == "pdf")
        {
            if (job.PdfBytes == null || job.PdfBytes.Length == 0)
            {
                return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
                {
                    Title = "PDF already downloaded and cleared",
                    Detail = "This PDF binary was deleted from database storage after download. Click 'Regenerate' in Audit Files to rebuild it.",
                    Status = StatusCodes.Status410Gone
                });
            }

            var bytes = job.PdfBytes;
            var fileName = job.PdfFileName;
            if (!HttpMethods.IsHead(Request.Method))
            {
                job.PdfBytes = null;
                job.PdfDownloadedAtUtc = DateTime.UtcNow;
                await _context.SaveChangesAsync(ct);
            }
            return File(bytes, "application/pdf", fileName);
        }

        if (job.ExcelBytes == null || job.ExcelBytes.Length == 0)
        {
            return StatusCode(StatusCodes.Status410Gone, new ProblemDetails
            {
                Title = "Excel already downloaded and cleared",
                Detail = "This Excel binary was deleted from database storage after download. Click 'Regenerate' in Audit Files to rebuild it.",
                Status = StatusCodes.Status410Gone
            });
        }

        var xlsxBytes = job.ExcelBytes;
        var xlsxName = job.ExcelFileName;
        if (!HttpMethods.IsHead(Request.Method))
        {
            job.ExcelBytes = null;
            job.ExcelDownloadedAtUtc = DateTime.UtcNow;
            await _context.SaveChangesAsync(ct);
        }
        return File(
            xlsxBytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            xlsxName);
    }

    /// <summary>
    /// Strictly Admin-only: Deletes an Audit Files entry completely (including saved filter history).
    /// </summary>
    [HttpDelete("admin-files/{jobId:guid}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteAdminAuditFile(Guid jobId, CancellationToken ct)
    {
        var job = await _context.AuditReportJobs
            .FirstOrDefaultAsync(j => j.Id == jobId, ct);

        if (job == null)
        {
            return NotFound();
        }

        _context.AuditReportJobs.Remove(job);
        await _context.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task EnqueueAuditJobAsync(
        AuditReportJob auditJob,
        string tenantName,
        PaymentStatus? statusFilter,
        CancellationToken ct)
    {
        var configuredApiBase = _configuration["Email:PublicApiBaseUrl"] ?? _configuration["PUBLIC_API_BASE_URL"];
        var baseApiUrl = !string.IsNullOrWhiteSpace(configuredApiBase)
            ? configuredApiBase.TrimEnd('/')
            : $"{Request.Scheme}://{Request.Host}";

        var configuredFrontendUrl = _configuration["Email:FrontendUrl"] ?? _configuration["FRONTEND_URL"];
        var frontendUrl = !string.IsNullOrWhiteSpace(configuredFrontendUrl)
            ? configuredFrontendUrl.TrimEnd('/')
            : Request.Headers.Origin.FirstOrDefault();

        var jobRequest = new ExportJobRequest(
            JobId: auditJob.Id.ToString("N"),
            TenantId: auditJob.TenantId,
            TenantName: tenantName,
            Year: auditJob.Year,
            RequestedByUserId: auditJob.RequestedByUserId,
            RequestedByEmail: auditJob.RequestedByEmail,
            QueuedAtUtc: auditJob.QueuedAtUtc,
            IsAuditPack: true,
            AuditReportJobId: auditJob.Id,
            AuditFilter: new AuditExportFilter(
                Year: auditJob.Year,
                StartDate: auditJob.StartDate,
                EndDate: auditJob.EndDate,
                Status: statusFilter,
                Category: auditJob.CategoryFilter,
                SiteId: auditJob.SiteId,
                Search: auditJob.SearchFilter
            ),
            RequestedByName: auditJob.RequestedByName,
            BaseApiUrl: baseApiUrl,
            FrontendUrl: frontendUrl
        );

        await _queue.QueueExportJobAsync(jobRequest, ct);
    }

    private static string GenerateSecureToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

    private static string BuildPeriodLabel(int year, DateOnly? startDate, DateOnly? endDate)
    {
        if (startDate.HasValue && endDate.HasValue)
        {
            return $"{startDate.Value:yyyy-MM-dd} to {endDate.Value:yyyy-MM-dd} (FY {year})";
        }
        if (startDate.HasValue)
        {
            return $"From {startDate.Value:yyyy-MM-dd} (FY {year})";
        }
        if (endDate.HasValue)
        {
            return $"Through {endDate.Value:yyyy-MM-dd} (FY {year})";
        }
        return $"Full Year FY {year}";
    }

    private static string BuildFilterSummary(AdminAuditPackRequest req, string? siteName)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(siteName)) parts.Add($"Site: {siteName}");
        if (req.Status.HasValue) parts.Add($"Status: {req.Status.Value}");
        if (!string.IsNullOrWhiteSpace(req.Category)) parts.Add($"Category: {req.Category.Trim()}");
        if (!string.IsNullOrWhiteSpace(req.Search)) parts.Add($"Search: \"{req.Search.Trim()}\"");
        return parts.Count == 0 ? "All Matching Expenses" : string.Join(" • ", parts);
    }

    private static AuditReportJobDto ToDto(AuditReportJob j) => new(
        Id: j.Id,
        ReportTitle: j.ReportTitle,
        PeriodLabel: j.PeriodLabel,
        FilterSummary: j.FilterSummary,
        Year: j.Year,
        StartDate: j.StartDate,
        EndDate: j.EndDate,
        StatusFilter: j.StatusFilter,
        CategoryFilter: j.CategoryFilter,
        SiteId: j.SiteId,
        SiteName: j.SiteName,
        SearchFilter: j.SearchFilter,
        Status: (ExportJobStatus)(int)j.Status,
        StatusName: j.Status.ToString(),
        TotalRecords: j.TotalRecords,
        TotalRequestedAmount: j.TotalRequestedAmount,
        TotalApprovedAmount: j.TotalApprovedAmount,
        PdfFileName: j.PdfFileName,
        ExcelFileName: j.ExcelFileName,
        HasPdf: j.Status == AuditReportStatus.Completed && j.PdfBytes != null,
        HasExcel: j.Status == AuditReportStatus.Completed && j.ExcelBytes != null,
        PdfDownloadedAtUtc: j.PdfDownloadedAtUtc,
        ExcelDownloadedAtUtc: j.ExcelDownloadedAtUtc,
        RequestedByName: j.RequestedByName,
        RequestedByEmail: j.RequestedByEmail,
        EmailDispatched: j.EmailDispatched,
        EmailDeliveryNote: j.EmailDeliveryNote,
        QueuedAtUtc: j.QueuedAtUtc,
        CompletedAtUtc: j.CompletedAtUtc,
        ErrorMessage: j.ErrorMessage
    );
}
