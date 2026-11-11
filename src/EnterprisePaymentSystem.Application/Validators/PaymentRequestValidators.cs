using FluentValidation;
using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Validators;

public class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(150).WithMessage("Title cannot exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than zero.")
            .PrecisionScale(14, 2, false).WithMessage("Amount must not exceed 14 digits with 2 decimal places.");

        RuleFor(x => x.Currency)
            .NotEmpty().WithMessage("Currency is required.")
            .Length(3).WithMessage("Currency must be a 3-letter ISO code (e.g., INR, USD).");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Category is required.");

        RuleFor(x => x.ExpenseYear)
            .InclusiveBetween(2000, 2100).WithMessage("Expense year must be valid.");

        // Strict Single-Year Boundary Validation
        RuleFor(x => x.StartDate)
            .Must((req, startDate) => startDate.Year == req.ExpenseYear)
            .WithMessage(req => $"StartDate ({req.StartDate:yyyy-MM-dd}) must fall strictly within the selected year {req.ExpenseYear}.");

        RuleFor(x => x.EndDate)
            .Must((req, endDate) => endDate.Year == req.ExpenseYear)
            .WithMessage(req => $"EndDate ({req.EndDate:yyyy-MM-dd}) must fall strictly within the selected year {req.ExpenseYear}.")
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate cannot be earlier than StartDate.");
    }
}

public class UpdatePaymentRequestValidator : AbstractValidator<UpdatePaymentRequest>
{
    public UpdatePaymentRequestValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithMessage("Title is required.")
            .MaximumLength(150).WithMessage("Title cannot exceed 150 characters.");

        RuleFor(x => x.Description)
            .MaximumLength(1000).WithMessage("Description cannot exceed 1000 characters.")
            .When(x => !string.IsNullOrEmpty(x.Description));

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than zero.")
            .PrecisionScale(14, 2, false).WithMessage("Amount must not exceed 14 digits with 2 decimal places.");

        RuleFor(x => x.ExpenseYear)
            .InclusiveBetween(2000, 2100).WithMessage("Expense year must be valid.");

        RuleFor(x => x.StartDate)
            .Must((req, startDate) => startDate.Year == req.ExpenseYear)
            .WithMessage(req => $"StartDate must fall strictly within the selected year {req.ExpenseYear}.");

        RuleFor(x => x.EndDate)
            .Must((req, endDate) => endDate.Year == req.ExpenseYear)
            .WithMessage(req => $"EndDate must fall strictly within the selected year {req.ExpenseYear}.")
            .GreaterThanOrEqualTo(x => x.StartDate)
            .WithMessage("EndDate cannot be earlier than StartDate.");

        RuleFor(x => x.Version)
            .GreaterThanOrEqualTo(0u).WithMessage("Concurrency Version token is required for updates.");
    }
}
