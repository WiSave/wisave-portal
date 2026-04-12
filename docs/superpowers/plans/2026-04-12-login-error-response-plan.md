# Login Error Response Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return structured `401` payloads from `POST /api/auth/login` that explain whether the login failed because the user was missing, the password was wrong, the account was locked, or sign-in is not allowed.

**Architecture:** Keep the existing auth endpoint shape and ASP.NET Identity flow. Add one typed auth-error DTO, drive the change from endpoint tests, then update the login handler to translate Identity outcomes into stable failure codes and messages while leaving successful responses unchanged.

**Tech Stack:** ASP.NET Core minimal APIs, ASP.NET Identity, xUnit, `WebApplicationFactory`

---

### Task 1: Add failing tests for detailed login failures

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Test: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`

- [ ] **Step 1: Write the failing tests**

Add assertions that deserialize the `401` body into the new auth-error DTO and check both `code` and `message` for:

```csharp
[Fact]
public async Task Login_UnknownEmail_Returns401WithUserNotFoundError()
{
    var client = CreateClient();

    var response = await PostWithAntiforgeryAsync(
        client,
        "/api/auth/login",
        new LoginRequest("missing@example.com", "Password123!"));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

    var error = await response.Content.ReadFromJsonAsync<AuthErrorResponse>(CancellationToken);
    Assert.NotNull(error);
    Assert.Equal("USER_NOT_FOUND", error.Code);
}

[Fact]
public async Task Login_InvalidPassword_Returns401WithInvalidPasswordError()
{
    var client = CreateClient();
    await RegisterAsync(client, new RegisterRequest("User", "wrong@example.com", "Password123!", "free"));

    var response = await PostWithAntiforgeryAsync(
        client,
        "/api/auth/login",
        new LoginRequest("wrong@example.com", "WrongPassword!"));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

    var error = await response.Content.ReadFromJsonAsync<AuthErrorResponse>(CancellationToken);
    Assert.NotNull(error);
    Assert.Equal("INVALID_PASSWORD", error.Code);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests.Login_UnknownEmail_Returns401WithUserNotFoundError|FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests.Login_InvalidPassword_Returns401WithInvalidPasswordError"`
Expected: FAIL because `AuthErrorResponse` does not exist and the endpoint returns an empty `401` body.

- [ ] **Step 3: Add the lockout error test**

Extend the existing lockout flow with:

```csharp
var error = await stillLocked.Content.ReadFromJsonAsync<AuthErrorResponse>(CancellationToken);
Assert.NotNull(error);
Assert.Equal("LOCKED_OUT", error.Code);
```

- [ ] **Step 4: Run the focused auth tests and confirm they still fail for the intended reason**

Run: `dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests.Login_"`
Expected: FAIL on missing typed error contract or missing response body assertions.

### Task 2: Implement the typed auth failure contract

**Files:**
- Modify: `src/WiSave.Portal/Auth/Models/AuthDtos.cs`
- Modify: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`
- Test: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`

- [ ] **Step 1: Add the DTO**

Add:

```csharp
public record AuthErrorResponse(string Code, string Message);
```

- [ ] **Step 2: Update endpoint metadata and login branching**

Use typed `401` results from the login handler:

```csharp
.Produces<AuthErrorResponse>(401)
```

and:

```csharp
if (user is null)
{
    return Results.Json(
        new AuthErrorResponse("USER_NOT_FOUND", "No account exists for that email address."),
        statusCode: StatusCodes.Status401Unauthorized);
}

if (result.IsLockedOut)
{
    return Results.Json(
        new AuthErrorResponse("LOCKED_OUT", "This account is locked out."),
        statusCode: StatusCodes.Status401Unauthorized);
}

if (result.IsNotAllowed)
{
    return Results.Json(
        new AuthErrorResponse("NOT_ALLOWED", "Sign-in is not allowed for this account."),
        statusCode: StatusCodes.Status401Unauthorized);
}

if (!result.Succeeded)
{
    return Results.Json(
        new AuthErrorResponse("INVALID_PASSWORD", "The password is incorrect."),
        statusCode: StatusCodes.Status401Unauthorized);
}
```

- [ ] **Step 3: Run the focused tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests.Login_"`
Expected: PASS for the login response tests.

### Task 3: Verify the broader auth surface

**Files:**
- Test: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`

- [ ] **Step 1: Run the full auth endpoint test class**

Run: `dotnet test --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"`
Expected: PASS with no auth regressions.
