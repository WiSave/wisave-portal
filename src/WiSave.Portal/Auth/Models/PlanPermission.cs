namespace WiSave.Portal.Auth.Models;

public class PlanPermission
{
    public required string PlanId { get; set; }
    public Guid PermissionId { get; set; }

    public Plan Plan { get; set; } = null!;
    public Permission Permission { get; set; } = null!;
}
