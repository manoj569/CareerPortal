using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class JobApplicationStatusHistory : BaseEntity
{
    public JobApplicationStatus? PreviousStatus { get; set; }
    public JobApplicationStatus NewStatus { get; set; }
    public DateTime ChangedAtUtc { get; set; }
    public string? InternalNote { get; set; }
    public Guid ApplicationId { get; set; }
    public JobApplication Application { get; set; } = null!;
    public Guid ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;
}
