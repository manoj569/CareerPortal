using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.Auditing;
using JobPortal.Domain.Entities;
using JobPortal.Persistence.Context;
using Microsoft.EntityFrameworkCore;

namespace JobPortal.Persistence.Repositories;

public sealed class AuditLogRepository(JobPortalDbContext context) : IAuditLogRepository
{
    public Task AddAsync(
        AuditLog auditLog, CancellationToken cancellationToken = default) =>
        context.AuditLogs.AddAsync(auditLog, cancellationToken).AsTask();

    public async Task<(IReadOnlyCollection<AuditLog> Items, int TotalCount)> SearchAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var source = context.AuditLogs.AsNoTracking().AsQueryable();
        if (query.ActorId.HasValue)
            source = source.Where(log => log.UserId == query.ActorId);
        if (query.Action.HasValue)
            source = source.Where(log => log.Action == query.Action);
        if (!string.IsNullOrWhiteSpace(query.EntityType))
        {
            var entityType = query.EntityType.Trim();
            source = source.Where(log => log.EntityName == entityType);
        }
        if (!string.IsNullOrWhiteSpace(query.EntityId))
        {
            var entityId = query.EntityId.Trim();
            source = source.Where(log => log.EntityId == entityId);
        }
        if (query.FromUtc.HasValue)
            source = source.Where(log => log.CreatedAtUtc >= query.FromUtc);
        if (query.ToUtc.HasValue)
            source = source.Where(log => log.CreatedAtUtc <= query.ToUtc);
        if (!string.IsNullOrWhiteSpace(query.CorrelationId))
        {
            var correlationId = query.CorrelationId.Trim();
            source = source.Where(log => log.CorrelationId == correlationId);
        }

        var totalCount = await source.CountAsync(cancellationToken);
        var items = await source
            .OrderByDescending(log => log.CreatedAtUtc)
            .ThenByDescending(log => log.Id)
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArrayAsync(cancellationToken);
        return (items, totalCount);
    }
}
