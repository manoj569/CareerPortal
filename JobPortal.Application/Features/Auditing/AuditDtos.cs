using JobPortal.Domain.Enums;

namespace JobPortal.Application.Features.Auditing;

public sealed record AuditLogQuery(
    int PageNumber = 1,
    int PageSize = 20,
    Guid? ActorId = null,
    AuditAction? Action = null,
    string? EntityType = null,
    string? EntityId = null,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    string? CorrelationId = null);

public sealed record AuditLogResponse(
    Guid Id,
    Guid? ActorId,
    string? ActorRole,
    AuditAction Action,
    string EntityType,
    string EntityId,
    DateTime OccurredAtUtc,
    string? CorrelationId,
    IReadOnlyDictionary<string, string?> Metadata);
