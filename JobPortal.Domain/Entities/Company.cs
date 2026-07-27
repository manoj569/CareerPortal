using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class Company : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? LogoUrl { get; set; }
    public string? Industry { get; set; }
    public string? Location { get; set; }
    public int? EmployeeCount { get; set; }
    public bool IsVerified { get; set; }
    public Guid OwnerUserId { get; set; }
    public User OwnerUser { get; set; } = null!;
    public ICollection<Job> Jobs { get; set; } = new List<Job>();
    public ICollection<Membership> Memberships { get; set; } = new List<Membership>();
}
