namespace WiSave.Portal.Contracts.Identity;

/// <summary>
/// Reads a <see cref="ForwardedUserContext"/> from forwarded portal headers.
/// </summary>
public static class ForwardedUserContextReader
{
    /// <summary>
    /// Reads a forwarded user context from the supplied headers.
    /// </summary>
    /// <param name="headers">The header collection to read.</param>
    /// <returns>
    /// A parsed <see cref="ForwardedUserContext"/> when a user identifier is present; otherwise, <see langword="null"/>.
    /// </returns>
    public static ForwardedUserContext? Read(IReadOnlyDictionary<string, string[]> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);

        var userId = ReadSingle(headers, PortalHeaderNames.UserId);
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        var email = ReadSingle(headers, PortalHeaderNames.UserEmail);
        return new ForwardedUserContext(
            userId,
            string.IsNullOrWhiteSpace(email) ? null : email,
            ReadSet(headers, PortalHeaderNames.UserPermissions),
            ReadSet(headers, PortalHeaderNames.UserRoles));
    }

    private static string? ReadSingle(IReadOnlyDictionary<string, string[]> headers, string headerName)
    {
        var values = ReadValues(headers, headerName);
        if (values is null || values.Length == 0)
        {
            return null;
        }

        return values[0];
    }

    private static IReadOnlySet<string> ReadSet(IReadOnlyDictionary<string, string[]> headers, string headerName)
    {
        var values = ReadValues(headers, headerName);
        if (values is null || values.Length == 0)
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return values
            .SelectMany(static value => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string[]? ReadValues(IReadOnlyDictionary<string, string[]> headers, string headerName)
    {
        if (headers.TryGetValue(headerName, out var values))
        {
            return values;
        }

        foreach (var pair in headers)
        {
            if (string.Equals(pair.Key, headerName, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }
}
