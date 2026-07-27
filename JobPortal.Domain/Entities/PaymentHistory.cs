using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class PaymentHistory : BaseEntity
{
    public PaymentStatus? PreviousStatus { get; set; }
    public PaymentStatus CurrentStatus { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public string? ProviderEventId { get; set; }
    public string? Reason { get; set; }
    public Guid PaymentId { get; set; }
    public Payment Payment { get; set; } = null!;
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
}
