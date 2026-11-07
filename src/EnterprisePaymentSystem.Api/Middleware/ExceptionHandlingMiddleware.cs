using System.Text.Json;
using EnterprisePaymentSystem.Domain.Exceptions;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace EnterprisePaymentSystem.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        ProblemDetails problem;

        switch (exception)
        {
            case ConcurrencyConflictException cce:
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                problem = new ProblemDetails
                {
                    Title = "Concurrency Conflict",
                    Status = StatusCodes.Status409Conflict,
                    Detail = cce.Message,
                    Instance = context.Request.Path
                };
                break;

            case Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException dce:
                _logger.LogWarning(dce, "DbUpdateConcurrencyException at {Path}: {Message}", context.Request.Path, dce.Message);
                context.Response.StatusCode = StatusCodes.Status409Conflict;
                problem = new ProblemDetails
                {
                    Title = "Concurrency Conflict",
                    Status = StatusCodes.Status409Conflict,
                    Detail = "The record was modified by another user or session. Please refresh and try again.",
                    Instance = context.Request.Path
                };
                break;

            case DomainException de:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                problem = new ProblemDetails
                {
                    Title = "Business Rule Violation",
                    Status = StatusCodes.Status400BadRequest,
                    Detail = de.Message,
                    Instance = context.Request.Path
                };
                break;

            case ValidationException ve:
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var errors = ve.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());

                problem = new ValidationProblemDetails(errors)
                {
                    Title = "Validation Failed",
                    Status = StatusCodes.Status400BadRequest,
                    Instance = context.Request.Path
                };
                break;

            default:
                _logger.LogError(exception, "Unhandled exception occurred at {Path}", context.Request.Path);
                context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                problem = new ProblemDetails
                {
                    Title = "Internal Server Error",
                    Status = StatusCodes.Status500InternalServerError,
                    Detail = "An unexpected error occurred. Please contact system support.",
                    Instance = context.Request.Path
                };
                break;
        }

        var json = JsonSerializer.Serialize(problem);
        await context.Response.WriteAsync(json);
    }
}
