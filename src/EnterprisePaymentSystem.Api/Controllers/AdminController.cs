using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EnterprisePaymentSystem.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "FinanceAdmin,TenantAdmin")]
public class AdminController : ControllerBase
{
    private readonly IPaymentService _paymentService;

    public AdminController(IPaymentService paymentService)
    {
        _paymentService = paymentService;
    }

    /// <summary>
    /// Gets aggregated finance KPIs and metric breakdown for the selected year.
    /// </summary>
    [HttpGet("metrics")]
    public async Task<ActionResult<FinanceMetricsDto>> GetMetrics(
        [FromQuery] int year = 2026,
        [FromQuery] DateOnly? startDate = null,
        [FromQuery] DateOnly? endDate = null,
        CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100)
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid year parameter",
                Detail = "Year must be between 2000 and 2100.",
                Status = StatusCodes.Status400BadRequest
            });
        }

        var metrics = await _paymentService.GetFinanceMetricsAsync(year, startDate, endDate, ct);
        return Ok(metrics);
    }
}
