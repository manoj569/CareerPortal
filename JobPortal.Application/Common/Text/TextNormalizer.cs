namespace JobPortal.Application.Common.Text;

public static class TextNormalizer
{
    public static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
