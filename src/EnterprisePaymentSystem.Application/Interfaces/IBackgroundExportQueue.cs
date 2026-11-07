using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IBackgroundExportQueue
{
    ValueTask QueueExportJobAsync(ExportJobRequest job, CancellationToken cancellationToken = default);
    ValueTask<ExportJobRequest> DequeueExportJobAsync(CancellationToken cancellationToken = default);
    void UpdateJobStatus(string jobId, ExportJobStatus status, string? downloadUrl = null, string? errorMessage = null);
    ExportJobStatusResponse? GetJobStatus(string jobId);
}
