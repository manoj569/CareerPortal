using Serilog.Context;

namespace JobPortal.API.Middleware;

public sealed class SecurityHeadersMiddleware(RequestDelegate next, IHostEnvironment environment)
{
    private const string CorrelationHeader = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var supplied = context.Request.Headers[CorrelationHeader].FirstOrDefault();
        var correlationId = IsSafeCorrelationId(supplied) ? supplied! : context.TraceIdentifier;
        context.TraceIdentifier = correlationId;
        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        context.Response.Headers["Content-Security-Policy"] =
            environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger")
                ? "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'"
                : "default-src 'none'; frame-ancestors 'none'";
        context.Response.Headers["Cross-Origin-Resource-Policy"] = "same-site";
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";
        if (context.Request.IsHttps)
            context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000; includeSubDomains";

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("ClientIp", context.Connection.RemoteIpAddress?.ToString()))
            await next(context);
    }

    private static bool IsSafeCorrelationId(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 64 &&
        value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_' or '.');
}
