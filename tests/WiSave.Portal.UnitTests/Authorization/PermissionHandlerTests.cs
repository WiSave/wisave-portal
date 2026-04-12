using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Authorization;
using Xunit;

namespace WiSave.Portal.UnitTests.Authorization;

public class PermissionHandlerTests
{
    [Fact]
    public async Task Handle_WildcardPermission_Succeeds()
    {
        var handler = new PermissionHandler();
        var requirement = new PermissionRequirement("expenses");
        var context = CreateContext(requirement, ["*"]);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_MatchingPrefix_Succeeds()
    {
        var handler = new PermissionHandler();
        var requirement = new PermissionRequirement("expenses");
        var context = CreateContext(requirement, [PortalPermissions.Expenses.Read, PortalPermissions.Incomes.Read]);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_NoMatchingPrefix_Fails()
    {
        var handler = new PermissionHandler();
        var requirement = new PermissionRequirement("expenses");
        var context = CreateContext(requirement, [PortalPermissions.Incomes.Read, PortalPermissions.Stocks.Read]);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_EmptyPermissions_Fails()
    {
        var handler = new PermissionHandler();
        var requirement = new PermissionRequirement("expenses");
        var context = CreateContext(requirement, []);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Handle_PrefixWithoutDelimiter_DoesNotMatch()
    {
        var handler = new PermissionHandler();
        var requirement = new PermissionRequirement("exp");
        var context = CreateContext(requirement, [PortalPermissions.Expenses.Read]);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private static AuthorizationHandlerContext CreateContext(
        PermissionRequirement requirement, HashSet<string> permissions)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Items["UserPermissions"] = permissions;

        var claimsPrincipal = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity("test"));

        return new AuthorizationHandlerContext(
            [requirement], claimsPrincipal, httpContext);
    }
}
