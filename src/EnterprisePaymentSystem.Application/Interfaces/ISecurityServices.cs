using EnterprisePaymentSystem.Domain.Entities;

namespace EnterprisePaymentSystem.Application.Interfaces;

public interface IJwtTokenGenerator
{
    string GenerateToken(User user, string tenantName);
}

public interface IPasswordHasher
{
    string HashPassword(string password);
    bool VerifyPassword(string password, string passwordHash);
}
