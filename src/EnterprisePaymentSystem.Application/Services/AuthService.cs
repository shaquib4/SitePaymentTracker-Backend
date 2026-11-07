using EnterprisePaymentSystem.Application.DTOs;
using EnterprisePaymentSystem.Application.Interfaces;
using EnterprisePaymentSystem.Application.Interfaces.Repositories;
using EnterprisePaymentSystem.Domain.Exceptions;

namespace EnterprisePaymentSystem.Application.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;
    private readonly ICurrentUserService _currentUserService;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator,
        ICurrentUserService currentUserService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
        _currentUserService = currentUserService;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            throw new DomainException("Email and password are required.");
        }

        var user = await _userRepository.GetByEmailAsync(request.Email, ct);
        if (user == null || !_passwordHasher.VerifyPassword(request.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException("Invalid email or password.");
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedAccessException("Your user account has been deactivated.");
        }

        var tenantName = user.Tenant?.Name ?? "Organization";
        var token = _jwtTokenGenerator.GenerateToken(user, tenantName);

        return new LoginResponse(
            Token: token,
            UserId: user.Id,
            FullName: user.FullName,
            Email: user.Email,
            Role: user.Role.ToString(),
            TenantId: user.TenantId,
            TenantName: tenantName
        );
    }

    public async Task<UserDto?> GetCurrentUserAsync(CancellationToken ct = default)
    {
        var userId = _currentUserService.UserId;
        if (!userId.HasValue)
        {
            return null;
        }

        var user = await _userRepository.GetByIdAsync(userId.Value, ct);
        if (user == null)
        {
            return null;
        }

        return new UserDto(
            user.Id,
            user.Email,
            user.FullName,
            user.Role,
            user.Department,
            user.TenantId
        );
    }
}
