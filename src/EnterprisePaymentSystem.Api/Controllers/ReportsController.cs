using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController : ControllerBase
{
    private readonly IBackgroundExportQueue _queue;
    private readonly ICurrentUserService _currentUserService;
    private readonly IValidator<ExportReportRequest> _validator;
    private readonly string _storageDirectory;

    public ReportsController(
        IBackgroundExportQueue queue,
        ICurrentUserService currentUserService,
        IValidator<ExportReportRequest> validator)
    {
        _queue = queue;
        _currentUserService = currentUserService;
        _validator = validator;
        var defaultDir = Path.Combine(AppContext.BaseDirectory, "Storage", "Reports");
        _storageDirectory = Directory.Exists(defaultDir)
            ? defaultDir
            : Path.Combine(Path.GetTempPath(), "Storage", "Reports");
    }

    /// <summary>
    /// Drops export job in background queue, decoupling HTTP request from heavy Excel generation. Returns 202 Accepted.
    /// </summary>
    [HttpPost("export-async")]
    [Authorize(Roles = "FinanceAdmin,TenantAdmin")]
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

        // Returns HTTP 202 Accepted with location of status check
        return Accepted($"/api/reports/status/{jobId}", status);
    }

    /// <summary>
    /// Checks status of the queued Excel generation job.
    /// </summary>
    [HttpGet("status/{jobId}")]
    [Authorize(Roles = "FinanceAdmin,TenantAdmin")]
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
    [AllowAnonymous] // JobId acts as a secure unique token
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
            $"ExpenseReport_{jobId.Substring(0, 8)}.xlsx"
        );
    }
}
