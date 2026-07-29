using JobPortal.Application.Features.Auditing;
using JobPortal.Domain.Entities;

namespace JobPortal.Application.Abstractions.Persistence;

public interface IAuditLogRepository
{
    Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken = default);
    Task<(IReadOnlyCollection<AuditLog> Items, int TotalCount)> SearchAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default);
}
