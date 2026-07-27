using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class RefreshToken : BaseEntity
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public string? ReplacedByToken { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
