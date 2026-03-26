namespace WiSave.Portal.Auth.Models;

public class Plan
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<PlanPermission> PlanPermissions { get; set; } = [];
}
