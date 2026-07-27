using System.Security.Claims;
using JobPortal.Application.Common.Exceptions;

namespace JobPortal.API.Extensions;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal principal) =>
        principal.TryGetUserId() ?? throw new UnauthorizedException();

    public static Guid? TryGetUserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
}
