namespace WiSave.Portal.Contracts.Identity;

/// <summary>
/// Represents the authenticated user context forwarded by the portal to downstream services.
/// </summary>
/// <param name="UserId">The unique identifier of the authenticated user.</param>
/// <param name="Email">The email address of the authenticated user, when available.</param>
/// <param name="Permissions">The permissions granted to the user for downstream authorization.</param>
/// <param name="Roles">The roles assigned to the user.</param>
public sealed record ForwardedUserContext(
    string UserId,
    string? Email,
    IReadOnlySet<string> Permissions,
    IReadOnlySet<string> Roles);
