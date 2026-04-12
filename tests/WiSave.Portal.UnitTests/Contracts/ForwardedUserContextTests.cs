using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Contracts.Identity;
using Xunit;

namespace WiSave.Portal.UnitTests.Contracts;

public sealed class ForwardedUserContextTests
{
    [Fact]
    public void Read_ReturnsContext_ForValidHeaders()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserId] = ["user-1"],
            [PortalHeaderNames.UserEmail] = ["user@example.com"],
            [PortalHeaderNames.UserPermissions] =
                [$"{PortalPermissions.Expenses.Read}, {PortalPermissions.Expenses.Write}"],
            [PortalHeaderNames.UserRoles] = ["admin, user"],
        };

        var context = ForwardedUserContextReader.Read(headers);

        Assert.NotNull(context);
        Assert.Equal("user-1", context.UserId);
        Assert.Equal("user@example.com", context.Email);
        Assert.Contains(PortalPermissions.Expenses.Read, context.Permissions);
        Assert.Contains(PortalPermissions.Expenses.Write, context.Permissions);
        Assert.Contains("admin", context.Roles);
        Assert.Contains("user", context.Roles);
    }

    [Fact]
    public void Read_ReturnsNull_WhenUserIdHeaderIsMissing()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserPermissions] = [PortalPermissions.Expenses.Read],
        };

        var context = ForwardedUserContextReader.Read(headers);

        Assert.Null(context);
    }

    [Fact]
    public void Read_AllowsMissingOptionalEmail()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserId] = ["user-1"],
            [PortalHeaderNames.UserPermissions] = [PortalPermissions.Expenses.Read],
        };

        var context = ForwardedUserContextReader.Read(headers);

        Assert.NotNull(context);
        Assert.Null(context.Email);
    }

    [Fact]
    public void Read_ParsesPermissionsAndRoles_CaseInsensitively()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserId] = ["user-1"],
            [PortalHeaderNames.UserPermissions] =
                [$" {PortalPermissions.Expenses.Read.ToUpperInvariant()} , {PortalPermissions.Expenses.Write} "],
            [PortalHeaderNames.UserRoles] = [" Admin , USER "],
        };

        var context = ForwardedUserContextReader.Read(headers);

        Assert.NotNull(context);
        Assert.Contains(PortalPermissions.Expenses.Read, context.Permissions);
        Assert.Contains(PortalPermissions.Expenses.Write, context.Permissions);
        Assert.Contains("admin", context.Roles);
        Assert.Contains("user", context.Roles);
    }

    [Fact]
    public void Write_UsesSharedHeaderNamesAndCommaSeparatedValues()
    {
        var context = new ForwardedUserContext(
            "user-1",
            "user@example.com",
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                PortalPermissions.Expenses.Read,
                PortalPermissions.Expenses.Write,
            },
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "admin",
                "user",
            });

        var headers = ForwardedUserContextWriter.Write(context);

        Assert.Equal("user-1", Assert.Single(headers[PortalHeaderNames.UserId]));
        Assert.Equal("user@example.com", Assert.Single(headers[PortalHeaderNames.UserEmail]));
        Assert.Contains(PortalPermissions.Expenses.Read, Assert.Single(headers[PortalHeaderNames.UserPermissions]));
        Assert.Contains(PortalPermissions.Expenses.Write, Assert.Single(headers[PortalHeaderNames.UserPermissions]));
        Assert.Contains("admin", Assert.Single(headers[PortalHeaderNames.UserRoles]));
        Assert.Contains("user", Assert.Single(headers[PortalHeaderNames.UserRoles]));
    }
}
