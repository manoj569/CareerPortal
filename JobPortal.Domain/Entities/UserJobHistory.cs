using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class UserJobHistory : BaseEntity
{
    public JobHistoryAction Action { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Notes { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
}
