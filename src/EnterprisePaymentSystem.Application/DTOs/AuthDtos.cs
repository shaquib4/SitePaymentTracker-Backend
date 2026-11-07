using EnterprisePaymentSystem.Domain.Enums;

namespace EnterprisePaymentSystem.Application.DTOs;

public record LoginRequest(string Email, string Password);

public record LoginResponse(
    string Token,
    Guid UserId,
    string FullName,
    string? Email,
    string Role,
    Guid TenantId,
    string TenantName
);

public record UserDto(
    Guid Id,
    string? Email,
    string FullName,
    UserRole Role,
    string Department,
    Guid TenantId
);
