using FluentValidation;
using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Validators;

public class ExportReportRequestValidator : AbstractValidator<ExportReportRequest>
{
    public ExportReportRequestValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100).WithMessage("Export year must be between 2000 and 2100.");
    }
}

public class AdminAuditPackRequestValidator : AbstractValidator<AdminAuditPackRequest>
{
    public AdminAuditPackRequestValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100).WithMessage("Year must be between 2000 and 2100.");
        RuleFor(x => x.EndDate)
            .GreaterThanOrEqualTo(x => x.StartDate!.Value)
            .When(x => x.StartDate.HasValue && x.EndDate.HasValue)
            .WithMessage("EndDate must be on or after StartDate.");
        RuleFor(x => x.RecipientEmail)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.RecipientEmail))
            .WithMessage("RecipientEmail must be a valid email address.");
    }
}

public class PaymentFilterQueryValidator : AbstractValidator<PaymentFilterQuery>
{
    public PaymentFilterQueryValidator()
    {
        RuleFor(x => x.Year)
            .InclusiveBetween(2000, 2100).WithMessage("Year is required and must be between 2000 and 2100.");
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1).WithMessage("Page must be at least 1.");
        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 200).WithMessage("PageSize must be between 1 and 200.");
    }
}
