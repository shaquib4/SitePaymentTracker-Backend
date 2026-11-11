using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Domain.Entities;
using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.Helpers;

public static class PaymentMapper
{
    public static PaymentResponseDto MapToDto(PaymentRequest p)
    {
        var auditDtos = (p.AuditLogs ?? Enumerable.Empty<PaymentAuditLog>())
            .OrderBy(a => a.TimestampUtc)
            .Select(a => new PaymentAuditLogDto(
                a.Id,
                a.Action,
                a.PreviousStatus,
                a.NewStatus,
                a.PerformedByUserId,
                a.PerformedByUserName,
                a.TimestampUtc,
                a.Comments
            )).ToList();

        var requestedAmount = p.RequestedAmount > 0m ? p.RequestedAmount : p.Amount;
        var approvedAmount = p.Status == PaymentStatus.Approved || p.Status == PaymentStatus.Completed
            ? p.Amount
            : (decimal?)null;

        return new PaymentResponseDto(
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
            AuditLogs: auditDtos,
            RequestedAmount: requestedAmount,
            ApprovedAmount: approvedAmount
        );
    }
}
