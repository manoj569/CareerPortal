using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IUserRepository
{
    Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default);
    Task<bool> RegistrationIdentityExistsAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default);
    Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default);
    Task AddAsync(User user, CancellationToken cancellationToken = default);
    void Update(User user);
}

public interface IRefreshTokenRepository
{
    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default);
    void Update(RefreshToken refreshToken);
    Task RevokeActiveForUserAsync(Guid userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
