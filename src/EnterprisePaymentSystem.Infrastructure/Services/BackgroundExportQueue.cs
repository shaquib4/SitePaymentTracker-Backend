using System.Collections.Concurrent;
using System.Threading.Channels;
using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;

namespace EnterprisePaymentSystem.Infrastructure.Services;

public class BackgroundExportQueue : IBackgroundExportQueue
{
    private readonly Channel<ExportJobRequest> _queue;
    private readonly ConcurrentDictionary<string, ExportJobStatusResponse> _jobStatuses = new();

    public BackgroundExportQueue()
    {
        // Unbounded channel for high-throughput non-blocking job drops
        _queue = Channel.CreateUnbounded<ExportJobRequest>(new UnboundedChannelOptions
        {
            SingleReader = true
        });
    }

    public async ValueTask QueueExportJobAsync(ExportJobRequest job, CancellationToken cancellationToken = default)
    {
        _jobStatuses[job.JobId] = new ExportJobStatusResponse(
            job.JobId,
            ExportJobStatus.Queued,
            ExportJobStatus.Queued.ToString(),
            job.Year,
            job.QueuedAtUtc,
            null,
            null
        );

        await _queue.Writer.WriteAsync(job, cancellationToken);
    }

    public async ValueTask<ExportJobRequest> DequeueExportJobAsync(CancellationToken cancellationToken = default)
    {
        return await _queue.Reader.ReadAsync(cancellationToken);
    }

    public void UpdateJobStatus(string jobId, ExportJobStatus status, string? downloadUrl = null, string? errorMessage = null)
    {
        if (_jobStatuses.TryGetValue(jobId, out var existing))
        {
            _jobStatuses[jobId] = existing with
            {
                Status = status,
                StatusName = status.ToString(),
                DownloadUrl = downloadUrl ?? existing.DownloadUrl,
                ErrorMessage = errorMessage ?? existing.ErrorMessage
            };
        }
    }

    public ExportJobStatusResponse? GetJobStatus(string jobId)
    {
        return _jobStatuses.TryGetValue(jobId, out var status) ? status : null;
    }
}
