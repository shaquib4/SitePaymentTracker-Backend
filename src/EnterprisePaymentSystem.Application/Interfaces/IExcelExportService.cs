using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IExcelExportService
{
    byte[] GenerateExpensesExcel(IReadOnlyList<PaymentResponseDto> payments, string tenantName, int year);
}
