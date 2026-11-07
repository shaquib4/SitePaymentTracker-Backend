using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.Interfaces.Repositories;

public interface IPaymentRepository
{
    Task<PaymentRequest?> GetByIdWithAuditLogsAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentRequest>> GetFilteredPaymentsAsync(
        int year,
        PaymentStatus? status = null,
        string? category = null,
        string? search = null,
        Guid? userIdFilter = null,
        CancellationToken ct = default);
    Task<IReadOnlyList<PaymentRequest>> GetPaymentsByYearAsync(int year, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentRequest>> GetPaymentsForExportAsync(Guid tenantId, int year, CancellationToken ct = default);
    Task AddAsync(PaymentRequest payment, CancellationToken ct = default);
    Task UpdateAsync(PaymentRequest payment, CancellationToken ct = default);
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
