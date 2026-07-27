using JobPortal.Domain.Common;
using JobPortal.Domain.Enums;

namespace JobPortal.Domain.Entities;

public sealed class Setting : BaseEntity
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string? Description { get; set; }
    public SettingScope Scope { get; set; } = SettingScope.Global;
    public Guid? UserId { get; set; }
    public User? User { get; set; }
}
