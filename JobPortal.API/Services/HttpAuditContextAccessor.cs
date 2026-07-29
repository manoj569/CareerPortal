using System.Security.Claims;
using JobPortal.Application.Abstractions.Auditing;

namespace JobPortal.API.Services;

public sealed class HttpAuditContextAccessor(
    IHttpContextAccessor httpContextAccessor) : IAuditContextAccessor
{
    private HttpContext? Context => httpContextAccessor.HttpContext;

    public Guid? ActorUserId =>
        Guid.TryParse(
            Context?.User.FindFirstValue(ClaimTypes.NameIdentifier),
            out var userId)
            ? userId
            : null;

    public string? ActorRole =>
        Context?.User.FindFirstValue(ClaimTypes.Role);

    public string? CorrelationId =>
        Context?.TraceIdentifier;
}
