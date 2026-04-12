namespace WiSave.Portal.Contracts.Identity;

/// <summary>
/// Writes a <see cref="ForwardedUserContext"/> into forwarded portal headers.
/// </summary>
public static class ForwardedUserContextWriter
{
    /// <summary>
    /// Writes the supplied context into a new header dictionary.
    /// </summary>
    /// <param name="context">The context to serialize.</param>
    /// <returns>A case-insensitive header dictionary containing the serialized context.</returns>
    public static IReadOnlyDictionary<string, string[]> Write(ForwardedUserContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserId] = [context.UserId],
        };

        if (!string.IsNullOrWhiteSpace(context.Email))
        {
            headers[PortalHeaderNames.UserEmail] = [context.Email];
        }

        if (context.Roles.Count > 0)
        {
            headers[PortalHeaderNames.UserRoles] = [string.Join(",", context.Roles)];
        }

        if (context.Permissions.Count > 0)
        {
            headers[PortalHeaderNames.UserPermissions] = [string.Join(",", context.Permissions)];
        }

        return headers;
    }
}
