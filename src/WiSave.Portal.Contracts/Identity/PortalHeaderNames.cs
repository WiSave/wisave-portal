namespace WiSave.Portal.Contracts.Identity;

/// <summary>
/// Defines the header names used by the portal when forwarding authenticated user context.
/// </summary>
public static class PortalHeaderNames
{
    /// <summary>
    /// Gets the header that carries the user identifier.
    /// </summary>
    public const string UserId = "X-User-Id";

    /// <summary>
    /// Gets the header that carries the user email address.
    /// </summary>
    public const string UserEmail = "X-User-Email";

    /// <summary>
    /// Gets the header that carries the forwarded user roles.
    /// </summary>
    public const string UserRoles = "X-User-Roles";

    /// <summary>
    /// Gets the header that carries the forwarded permission set.
    /// </summary>
    public const string UserPermissions = "X-User-Permissions";
}
