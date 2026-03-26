namespace WiSave.Portal.Auth.Models;

public class Permission
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    public ICollection<PlanPermission> PlanPermissions { get; set; } = [];
}
