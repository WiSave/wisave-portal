using Microsoft.AspNetCore.Identity;

namespace WiSave.Portal.Auth.Models;

public class ApplicationUser : IdentityUser
{
    public required string Name { get; set; }
    public string PlanId { get; set; } = "free";
}
