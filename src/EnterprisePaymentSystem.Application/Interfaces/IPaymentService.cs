using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IPaymentService
{
    Task<PaymentResponseDto> CreateAsync(CreatePaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto> CreateWageRequestAsync(WagePaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto> UpdateAsync(Guid id, UpdatePaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentResponseDto>> GetListAsync(PaymentFilterQuery query, CancellationToken ct = default);
    Task<PaymentResponseDto> SubmitAsync(Guid id, string? comments = null, CancellationToken ct = default);
    Task<PaymentResponseDto> RequestInfoAsync(Guid id, RequestInfoPaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto> ApproveAsync(Guid id, ApprovePaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto> RejectAsync(Guid id, RejectPaymentRequest request, CancellationToken ct = default);
    Task<PaymentResponseDto> MarkPaidAsync(Guid id, MarkPaidRequest request, CancellationToken ct = default);
    Task<FinanceMetricsDto> GetFinanceMetricsAsync(int year, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentResponseDto>> GetExpensesForExportAsync(int year, CancellationToken ct = default);
}
