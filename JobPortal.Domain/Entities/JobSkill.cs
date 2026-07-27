using JobPortal.Domain.Common;

namespace JobPortal.Domain.Entities;

public sealed class JobSkill : BaseEntity
{
    public Guid JobId { get; set; }
    public Job Job { get; set; } = null!;
    public Guid SkillId { get; set; }
    public Skill Skill { get; set; } = null!;
    public bool IsRequired { get; set; }
    public byte ProficiencyLevel { get; set; }
}
