using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IAuditReportJobProcessor
{
    Task ProcessAuditPackJobAsync(ExportJobRequest job, CancellationToken ct = default);
}
