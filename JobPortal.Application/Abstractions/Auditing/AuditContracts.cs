using JobPortal.Application.Features.Auditing;
using JobPortal.Domain.Enums;
using JobPortal.Shared.Models;

namespace JobPortal.Application.Abstractions.Auditing;

public interface IAuditWriter
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken cancellationToken = default);
}

public interface IAuditContextAccessor
{
    Guid? ActorUserId { get; }
    string? ActorRole { get; }
    string? CorrelationId { get; }
}

public interface IAuditLogService
{
    Task<PagedResponse<AuditLogResponse>> SearchAsync(
        AuditLogQuery query, CancellationToken cancellationToken = default);
}

public sealed record AuditActor(Guid? UserId, string Role);

public sealed record AuditEvent(
    AuditAction Action,
    string EntityType,
    string EntityId,
    IReadOnlyDictionary<string, string?>? Metadata = null,
    AuditActor? Actor = null);
