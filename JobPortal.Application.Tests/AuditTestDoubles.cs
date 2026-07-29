using JobPortal.Application.Abstractions.Auditing;

namespace JobPortal.Application.Tests;

internal sealed class AuditWriterTestDouble : IAuditWriter
{
    public List<AuditEvent> Events { get; } = [];

    public Task AppendAsync(
        AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        Events.Add(auditEvent);
        return Task.CompletedTask;
    }
}
