using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class ApplicationQuotaUsage : BaseEntity
{
    public Guid UserId { get; set; }

    public User User { get; set; } = null!;

    public ApplicationQuotaPeriod Period { get; set; }

    // Stored in UTC. For Free: start of the IST month.
    // For Premium: start of the IST day.
    public DateTime PeriodStartsAtUtc { get; set; }

    public DateTime PeriodEndsAtUtc { get; set; }

    public int UsedApplications { get; set; }

    // Prevents two simultaneous Apply requests from exceeding the limit.
    public byte[] RowVersion { get; set; } = [];
}
