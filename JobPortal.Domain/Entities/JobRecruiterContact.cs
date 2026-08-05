using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class JobRecruiterContact : BaseEntity
{
    public Guid JobId { get; set; }

    public Job Job { get; set; } = null!;

    public string ContactName { get; set; } = string.Empty;

    public string ContactRole { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? PhoneNumber { get; set; }

    public bool IsSharingApproved { get; set; }
}
