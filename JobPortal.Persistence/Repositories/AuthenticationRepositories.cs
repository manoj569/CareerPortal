using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Common.Exceptions;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class UserRepository(JobPortalDbContext context) : IUserRepository
{
    public Task<User?> GetByNormalizedEmailAsync(string normalizedEmail, CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.NormalizedEmail == normalizedEmail, cancellationToken);

    public Task<bool> RegistrationIdentityExistsAsync(
        string normalizedEmail,
        string normalizedPhoneNumber,
        CancellationToken cancellationToken = default) =>
        context.Users.AnyAsync(
            user => user.NormalizedEmail == normalizedEmail ||
                user.NormalizedPhoneNumber == normalizedPhoneNumber,
            cancellationToken);

    public Task<User?> GetByIdWithRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.Include(x => x.Role).SingleOrDefaultAsync(x => x.Id == userId, cancellationToken);

    public async Task AddAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.Role is not null)
        {
            context.Attach(user.Role);
        }

        await context.Users.AddAsync(user, cancellationToken);
    }

    public void Update(User user) => context.Users.Update(user);
}

public sealed class RefreshTokenRepository(JobPortalDbContext context) : IRefreshTokenRepository
{
    public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.Include(x => x.User).ThenInclude(x => x.Role).SingleOrDefaultAsync(x => x.Token == tokenHash, cancellationToken);

    public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
        context.RefreshTokens.AddAsync(refreshToken, cancellationToken).AsTask();

    public void Update(RefreshToken refreshToken) => context.RefreshTokens.Update(refreshToken);

    public Task RevokeActiveForUserAsync(
        Guid userId, DateTime revokedAtUtc, CancellationToken cancellationToken = default) =>
        context.RefreshTokens
            .Where(x => x.UserId == userId && x.RevokedAtUtc == null && x.ExpiresAtUtc > revokedAtUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(x => x.RevokedAtUtc, revokedAtUtc)
                .SetProperty(x => x.UpdatedAtUtc, revokedAtUtc), cancellationToken);
}

public sealed class UnitOfWork(JobPortalDbContext context) : IUnitOfWork
{
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is Microsoft.Data.SqlClient.SqlException
            { Number: 2601 or 2627 })
        {
            throw new UniqueConstraintException(
                "A database uniqueness constraint was violated.", exception);
        }
    }
}
