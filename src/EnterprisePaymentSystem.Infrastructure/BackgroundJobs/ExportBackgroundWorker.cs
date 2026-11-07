using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EnterprisePaymentSystem.Infrastructure.BackgroundJobs;

public class ExportBackgroundWorker : BackgroundService
{
    private readonly IBackgroundExportQueue _queue;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ExportBackgroundWorker> _logger;
    private readonly string _storageDirectory;

    public ExportBackgroundWorker(
        IBackgroundExportQueue queue,
        IServiceProvider serviceProvider,
        ILogger<ExportBackgroundWorker> logger)
    {
        _queue = queue;
        _serviceProvider = serviceProvider;
        _logger = logger;

        try
        {
            _storageDirectory = Path.Combine(AppContext.BaseDirectory, "Storage", "Reports");
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Storage directory could not be created in AppContext.BaseDirectory. Falling back to temporary directory.");
            _storageDirectory = Path.Combine(Path.GetTempPath(), "Storage", "Reports");
            if (!Directory.Exists(_storageDirectory))
            {
                Directory.CreateDirectory(_storageDirectory);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ExportBackgroundWorker started. Listening on Channel<ExportJobRequest>...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var job = await _queue.DequeueExportJobAsync(stoppingToken);

                _logger.LogInformation("Processing async report job {JobId} for Tenant '{TenantName}' ({TenantId}), Year {Year}",
                    job.JobId, job.TenantName, job.TenantId, job.Year);

                _queue.UpdateJobStatus(job.JobId, ExportJobStatus.Processing);

                using var scope = _serviceProvider.CreateScope();
                var paymentRepository = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
                var excelService = scope.ServiceProvider.GetRequiredService<IExcelExportService>();

                // Fetch records using repository
                var payments = await paymentRepository.GetPaymentsForExportAsync(job.TenantId, job.Year, stoppingToken);

                var dtos = payments.Select(p => new PaymentResponseDto(
                    Id: p.Id,
                    TenantId: p.TenantId,
                    UserId: p.UserId,
                    SubmitterName: p.User?.FullName ?? "Unknown",
                    SubmitterEmail: p.User?.Email ?? "Unknown",
                    SubmitterPhone: p.User?.PhoneNumber ?? string.Empty,
                    Title: p.Title,
                    Description: p.Description,
                    Amount: p.Amount,
                    Currency: p.Currency,
                    Category: p.Category,
                    CategoryId: p.CategoryId,
                    SiteId: p.SiteId,
                    SiteName: p.Site?.Name,
                    LabourerId: p.LabourerId,
                    LabourerName: p.Labourer?.Name,
                    ExpenseYear: p.ExpenseYear,
                    StartDate: p.StartDate,
                    EndDate: p.EndDate,
                    Status: p.Status,
                    StatusName: p.Status.ToString(),
                    InvoiceNumber: p.InvoiceNumber,
                    ReceiptAttachmentPath: p.ReceiptAttachmentPath,
                    RejectionReason: p.RejectionReason,
                    InfoRequestDetails: p.InfoRequestDetails,
                    ApprovalComments: p.ApprovalComments,
                    CreatedAtUtc: p.CreatedAtUtc,
                    UpdatedAtUtc: p.UpdatedAtUtc,
                    Version: p.Version,
                    AuditLogs: Array.Empty<PaymentAuditLogDto>()
                )).ToList();

                // ClosedXML Generation
                var excelBytes = excelService.GenerateExpensesExcel(dtos, job.TenantName, job.Year);

                // Save to local storage
                var filePath = Path.Combine(_storageDirectory, $"{job.JobId}.xlsx");
                await File.WriteAllBytesAsync(filePath, excelBytes, stoppingToken);

                // Simulate Resend API Email dispatch
                _logger.LogInformation("[Resend API Email Delivery] Dispatched report for Year {Year} to {Email}. File size: {Size} bytes.",
                    job.Year, job.RequestedByEmail, excelBytes.Length);

                var downloadUrl = $"/api/reports/download/{job.JobId}";
                _queue.UpdateJobStatus(job.JobId, ExportJobStatus.Completed, downloadUrl);

                _logger.LogInformation("Completed report job {JobId}. Download ready at {Url}", job.JobId, downloadUrl);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing export job in background worker.");
            }
        }

        _logger.LogInformation("ExportBackgroundWorker stopping gracefully.");
    }
}
