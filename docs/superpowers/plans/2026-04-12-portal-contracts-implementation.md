# WiSave.Portal.Contracts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a new `WiSave.Portal.Contracts` class library in this repo and switch portal auth-header forwarding and permission constants to use it.

**Architecture:** Introduce a pure .NET contracts assembly with XML-documented boundary types and constants, then consume it from `WiSave.Portal` without changing runtime behavior. Verify behavior with tests-first updates around header forwarding and contract parsing.

**Tech Stack:** .NET 10, xUnit v3, ASP.NET Core, YARP

---

### Task 1: Add Contract Parsing Tests

**Files:**
- Create: `tests/WiSave.Portal.Tests/Contracts/ForwardedUserContextTests.cs`
- Modify: `tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`
- Modify: `WiSave.Portal.slnx`
- Create: `src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj`

- [ ] **Step 1: Write the failing test**

```csharp
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Contracts.Identity;

namespace WiSave.Portal.Tests.Contracts;

public sealed class ForwardedUserContextTests
{
    [Fact]
    public void Read_ReturnsContext_ForValidHeaders()
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            [PortalHeaderNames.UserId] = ["user-1"],
            [PortalHeaderNames.UserEmail] = ["user@example.com"],
            [PortalHeaderNames.UserPermissions] = [$"{PortalPermissions.Expenses.Read}, {PortalPermissions.Expenses.Write}"],
            [PortalHeaderNames.UserRoles] = ["admin, user"]
        };

        var context = ForwardedUserContextReader.Read(headers);

        Assert.NotNull(context);
        Assert.Equal("user-1", context.UserId);
        Assert.Equal("user@example.com", context.Email);
        Assert.Contains(PortalPermissions.Expenses.Read, context.Permissions);
        Assert.Contains("admin", context.Roles);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~ForwardedUserContextTests`
Expected: FAIL because `WiSave.Portal.Contracts` types do not exist yet.

- [ ] **Step 3: Add empty project plumbing**

Create the contracts project, add a project reference from tests, and add the project to `WiSave.Portal.slnx` so the test project can compile against it.

- [ ] **Step 4: Run test to verify it still fails for missing implementation**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~ForwardedUserContextTests`
Expected: FAIL because reader/constants are not implemented yet.

### Task 2: Implement Contracts Assembly

**Files:**
- Create: `src/WiSave.Portal.Contracts/Authorization/PortalPermissions.cs`
- Create: `src/WiSave.Portal.Contracts/Identity/PortalHeaderNames.cs`
- Create: `src/WiSave.Portal.Contracts/Identity/ForwardedUserContext.cs`
- Create: `src/WiSave.Portal.Contracts/Identity/ForwardedUserContextReader.cs`
- Create: `src/WiSave.Portal.Contracts/Identity/ForwardedUserContextWriter.cs`
- Modify: `src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj`

- [ ] **Step 1: Write minimal implementation**

Implement the new contracts types with XML documentation on public types and members. Keep the API pure by using `IReadOnlyDictionary<string, string[]>` and `Dictionary<string, string[]>` rather than ASP.NET types.

- [ ] **Step 2: Run the focused contract test**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~ForwardedUserContextTests`
Expected: PASS

- [ ] **Step 3: Expand contract coverage**

Add tests for:
- missing optional email
- missing required user id returns `null`
- comma-separated permissions/roles are trimmed and case-insensitive
- writer emits the expected header names and values

- [ ] **Step 4: Run focused contract tests again**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~ForwardedUserContextTests`
Expected: PASS

### Task 3: Switch Portal Header Forwarding To Contracts

**Files:**
- Modify: `src/WiSave.Portal/Gateway/UserHeaderTransform.cs`
- Modify: `src/WiSave.Portal/WiSave.Portal.csproj`
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`

- [ ] **Step 1: Write the failing regression assertion**

Replace string literal header names in `UserHeaderTransformTests` with `PortalHeaderNames` and add an assertion that `X-User-Permissions` contains package-defined expenses permission constants when seeded through the new contract types.

- [ ] **Step 2: Run the gateway tests to verify failure if any contract assumptions are wrong**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~UserHeaderTransformTests`
Expected: FAIL until `UserHeaderTransform` uses the new shared writer correctly.

- [ ] **Step 3: Implement the portal wiring**

Update `UserHeaderTransform` to:
- use `PortalHeaderNames`
- create a `ForwardedUserContext`
- use `ForwardedUserContextWriter` to emit proxy headers

Add a project reference from `WiSave.Portal` to `WiSave.Portal.Contracts`.

- [ ] **Step 4: Run gateway tests again**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter FullyQualifiedName~UserHeaderTransformTests`
Expected: PASS

### Task 4: Replace Portal Permission String Literals In Code And Tests

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Authorization/PermissionHandlerTests.cs`
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`

- [ ] **Step 1: Write or adjust failing tests to use `PortalPermissions`**

Replace duplicated permission literals in tests with shared constants. Keep SQL scripts unchanged in this task.

- [ ] **Step 2: Run the targeted test set**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~PermissionHandlerTests|FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~UserHeaderTransformTests"`
Expected: PASS if runtime behavior is unchanged.

### Task 5: Final Verification

**Files:**
- Modify: `.gitignore`
- Create/Modify: files from Tasks 1-4

- [ ] **Step 1: Run the contracts and gateway-focused tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~ForwardedUserContextTests|FullyQualifiedName~UserHeaderTransformTests|FullyQualifiedName~PermissionHandlerTests"`
Expected: PASS

- [ ] **Step 2: Run the full portal test project if the targeted set is green**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`
Expected: PASS, or a clear report of any unrelated pre-existing failure.

- [ ] **Step 3: Summarize the implementation**

Report:
- files changed
- verification run
- remaining follow-up for `wisave-expenses`
