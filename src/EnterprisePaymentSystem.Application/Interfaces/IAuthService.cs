using EnterprisePaymentSystem.Application.DTOs;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default);
}
