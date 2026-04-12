using Microsoft.AspNetCore.Authorization;

namespace WiSave.Portal.Authorization;

public sealed class PermissionRequirement(string permissionPrefix) : IAuthorizationRequirement
{
    public string PermissionPrefix { get; } = permissionPrefix;
}
