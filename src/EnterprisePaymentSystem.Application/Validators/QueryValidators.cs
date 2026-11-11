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
