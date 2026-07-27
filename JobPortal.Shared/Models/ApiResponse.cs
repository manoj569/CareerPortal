namespace JobPortal.Shared.Models;

public sealed record ApiResponse<T>(T Data, string? Message = null);
public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int PageNumber, int PageSize, int TotalCount)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}
