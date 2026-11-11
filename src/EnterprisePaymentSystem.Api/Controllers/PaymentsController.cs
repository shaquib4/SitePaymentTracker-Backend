using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PaymentsController : ControllerBase
{
    private readonly IPaymentService _paymentService;
    private readonly IValidator<CreatePaymentRequest> _createValidator;
    private readonly IValidator<UpdatePaymentRequest> _updateValidator;
    private readonly IValidator<PaymentFilterQuery> _filterValidator;

    public PaymentsController(
        IPaymentService paymentService,
        IValidator<CreatePaymentRequest> createValidator,
        IValidator<UpdatePaymentRequest> updateValidator,
        IValidator<PaymentFilterQuery> filterValidator)
    {
        _paymentService = paymentService;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _filterValidator = filterValidator;
    }

    /// <summary>
    /// Fetches payments list strictly bounded by single-year filter validation.
    /// Accountants are restricted to the current active calendar year only.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedPaymentsResponseDto>> GetList(
        [FromQuery] PaymentFilterQuery query,
        CancellationToken ct)
    {
        var validationResult = await _filterValidator.ValidateAsync(query, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _paymentService.GetListAsync(query, ct);
        return Ok(result);
    }

    /// <summary>
    /// Aggregated Dashboard statistics for Construction Management.
    /// </summary>
    [HttpGet("dashboard-stats")]
    [Authorize(Roles = "Admin,Accountant")]
    public async Task<ActionResult<FinanceMetricsDto>> GetDashboardStats(
        [FromQuery] int year = 2026,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        var result = await _paymentService.GetFinanceMetricsAsync(year, startDate, endDate, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<PaymentResponseDto>> GetById(Guid id, CancellationToken ct)
    {
        var result = await _paymentService.GetByIdAsync(id, ct);
        if (result == null)
        {
            return NotFound(new ProblemDetails
            {
                Title = "Payment not found",
                Detail = $"Payment request with ID '{id}' was not found.",
                Status = StatusCodes.Status404NotFound
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Submits a standard expense claim (material, equipment, etc.)
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PaymentResponseDto>> Create(
        [FromBody] CreatePaymentRequest request,
        CancellationToken ct)
    {
        var validationResult = await _createValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _paymentService.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    /// <summary>
    /// Specific endpoint for Supervisor wage requests (requires LabourerId).
    /// </summary>
    [HttpPost("wage-request")]
    [Authorize(Roles = "Supervisor,Admin")]
    public async Task<ActionResult<PaymentResponseDto>> CreateWageRequest(
        [FromBody] WagePaymentRequest request,
        CancellationToken ct)
    {
        if (request.LabourerId == Guid.Empty || request.SiteId == Guid.Empty)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Both LabourerId and SiteId are mandatory for wage requests.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var result = await _paymentService.CreateWageRequestAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<PaymentResponseDto>> Update(
        Guid id,
        [FromBody] UpdatePaymentRequest request,
        CancellationToken ct)
    {
        var validationResult = await _updateValidator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            return BadRequest(new ValidationProblemDetails(validationResult.ToDictionary()));
        }

        var result = await _paymentService.UpdateAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Accountant requests more details on suspicious / unverified expenses.
    /// Changes status to InfoRequested and flags it for Admin to call employee.
    /// </summary>
    [HttpPost("{id:guid}/request-info")]
    [Authorize(Roles = "Accountant,Admin")]
    public async Task<ActionResult<PaymentResponseDto>> RequestInfo(
        Guid id,
        [FromBody] RequestInfoPaymentRequest request,
        CancellationToken ct)
    {
        var result = await _paymentService.RequestInfoAsync(id, request, ct);
        return Ok(result);
    }

    /// <summary>
    /// Admin approves payment request.
    /// Supports dynamic re-categorization or site assignment at moment of approval.
    /// </summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaymentResponseDto>> Approve(
        Guid id,
        [FromBody] ApprovePaymentRequest request,
        CancellationToken ct)
    {
        var result = await _paymentService.ApproveAsync(id, request, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaymentResponseDto>> Reject(
        Guid id,
        [FromBody] RejectPaymentRequest request,
        CancellationToken ct)
    {
        var result = await _paymentService.RejectAsync(id, request, ct);
        return Ok(result);
    }

    [HttpPost("{id:guid}/mark-paid")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PaymentResponseDto>> MarkPaid(
        Guid id,
        [FromBody] MarkPaidRequest request,
        CancellationToken ct)
    {
        var result = await _paymentService.MarkPaidAsync(id, request, ct);
        return Ok(result);
    }
}
