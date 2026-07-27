using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class SavedJob : BaseEntity
{
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
}
