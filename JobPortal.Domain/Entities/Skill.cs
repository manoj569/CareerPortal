using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class Skill : BaseEntity
{
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public ICollection<JobSkill> JobSkills { get; set; } = new List<JobSkill>();
}
