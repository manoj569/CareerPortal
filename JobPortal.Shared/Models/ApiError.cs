namespace JobPortal.Shared.Models;

public sealed record ApiError(string Code, string Message, IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ApiError InternalServerError() => new("internal_error", "An unexpected error occurred.");
}
