using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IExcelExportService
{
    byte[] GenerateExpensesExcel(IReadOnlyList<PaymentResponseDto> payments, string tenantName, int year);

    byte[] GenerateAuditExpensesExcel(
        IReadOnlyList<PaymentResponseDto> payments,
        string tenantName,
        int year,
        string periodLabel,
        string filterSummary);

    Task<byte[]> GenerateChunkedAuditExpensesExcelAsync(
        IAsyncEnumerable<IReadOnlyList<AuditExpenseRowDto>> rowChunks,
        string tenantName,
        int year,
        string periodLabel,
        string filterSummary,
        CancellationToken ct = default);
}
