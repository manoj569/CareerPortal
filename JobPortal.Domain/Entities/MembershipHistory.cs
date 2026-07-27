using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class MembershipHistory : BaseEntity
{
    public MembershipStatus? PreviousStatus { get; set; }
    public MembershipStatus CurrentStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? Reason { get; set; }
    public Guid MembershipId { get; set; }
    public Membership Membership { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
