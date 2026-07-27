using JobPortal.Application.Common.Exceptions;

namespace JobPortal.Application.Common.Validation;

public static class RequestGuards
{
    public const int MaximumPageSize = 100;

    public static void ValidatePagination(int pageNumber, int pageSize)
    {
        if (pageNumber < 1 || pageSize is < 1 or > MaximumPageSize)
            throw new BadRequestException(
                $"PageNumber must be positive and PageSize must be between 1 and {MaximumPageSize}.");
    }

    public static void ValidateLimit(int limit, int maximum)
    {
        if (maximum is < 1 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (limit < 1 || limit > maximum)
            throw new BadRequestException($"Limit must be between 1 and {maximum}.");
    }
}
