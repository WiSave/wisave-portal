# Auth and Antiforgery Alignment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the UI and portal consistently authenticate and authorize requests, and eliminate avoidable `400 Antiforgery token validation failed` responses in local development and authenticated app flows.

**Architecture:** Keep the portal as the source of truth for authentication, antiforgery, and route authorization. Align the UI transport layer so mutating calls flow through the same-origin `/api` path in development, and proactively bootstrap antiforgery cookies from both guest and authenticated shells so Angular can attach the `X-XSRF-TOKEN` header automatically. The key root cause is that Angular’s built-in XSRF support skips absolute or cross-origin API URLs, so `http://localhost:5100/api` bypasses the automatic header injection that works with same-origin `/api` requests.

**Tech Stack:** ASP.NET Core minimal APIs, ASP.NET Core Identity cookies, ASP.NET Core Antiforgery, YARP, Angular `HttpClient`, Angular XSRF configuration, xUnit.

---

## File Map

**Portal repository**
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs` — extend auth endpoint coverage for antiforgery bootstrap behavior if needed.
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs` — add or adjust an integration test proving proxied unsafe requests require a valid antiforgery token.
- Check: `src/WiSave.Portal/Program.cs` — keep antiforgery enforcement for unsafe proxied methods unchanged unless tests show a real server-side gap.

**UI repository**
- Modify: `public/env.js` — switch local default API base to `/api` so dev traffic uses the Angular proxy instead of cross-origin absolute URLs, or remove the local override entirely and fall back to the existing `/api` default in `runtime-config.ts`.
- Modify: `src/app/core/services/auth.service.ts` — add a focused method for fetching antiforgery cookies that both shells can reuse.
- Modify: `src/app/layout/auth-layout.component.ts` — keep antiforgery bootstrap for guest flows explicit without relying on `ngOnInit`.
- Modify: `src/app/layout/main-layout.component.ts` — add antiforgery bootstrap for authenticated shell startup without relying on `ngOnInit`.
- Check: `src/app/core/interceptors/auth.interceptor.ts` — keep credentials forwarding minimal; do not manually synthesize the XSRF header unless same-origin proxying proves insufficient.
- Modify: `README.md` — document the local dev requirement that `/api` must be used with the Angular proxy for cookie auth + XSRF, and note that Angular only sends the XSRF header on mutating requests.

## Constraints

- Preserve backend-first authorization; do not move security decisions into the UI.
- Do not weaken antiforgery validation in the portal to accommodate the current UI bug.
- Prefer same-origin `/api` transport over custom client-side XSRF-header logic.
- Keep changes surgical and close to existing patterns.
- Respect the UI guidance that discourages `ngOnInit` for simple initialization.
- Validate with targeted tests first.

### Task 1: Lock down current antiforgery behavior with backend gateway coverage

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Gateway/UserHeaderTransformTests.cs`
- Check: `src/WiSave.Portal/Program.cs`
- Check: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`

- [ ] **Step 1: Add a gateway integration test that proves unsafe proxied requests fail without antiforgery**

Use `/api/incomes` rather than `/api/expenses`, because the gateway test harness already points `incomes-cluster` at the downstream echo server and seeds `incomes:read` for the `free` plan.

```csharp
[Fact]
public async Task UnsafeProxyRequest_WithoutAntiforgeryToken_Returns400()
{
    var client = CreateClientWithCookies();
    await RegisterAsync(client, "Proxy User", "proxy@example.com");

    var response = await client.PostAsJsonAsync("/api/incomes", new { name = "Test" }, CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

- [ ] **Step 2: Run the new focused test and verify it fails only if the chosen downstream route shape is incompatible**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~UnsafeProxyRequest_WithoutAntiforgeryToken_Returns400"`
Expected: PASS after targeting an unsafe `incomes` route that reaches the echo server.

- [ ] **Step 3: Add the paired happy-path test proving the same proxied request forwards with antiforgery**

```csharp
[Fact]
public async Task UnsafeProxyRequest_WithAntiforgeryToken_ForwardsRequest()
{
    var client = CreateClientWithCookies();
    await RegisterAsync(client, "Proxy User", "proxy-ok@example.com");

    var token = await GetAntiforgeryTokenAsync(client);
    var message = new HttpRequestMessage(HttpMethod.Post, "/api/incomes");
    message.Headers.Add("X-XSRF-TOKEN", token);
    message.Content = JsonContent.Create(new { name = "Test" });

    var response = await client.SendAsync(message, CancellationToken);
    var forwarded = await response.Content.ReadFromJsonAsync<ForwardedRequest>(CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.NotNull(forwarded);
    Assert.Equal("/incomes", forwarded.Path);
}
```

- [ ] **Step 4: Run the focused gateway antiforgery tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~UnsafeProxyRequest_"`
Expected: PASS

- [ ] **Step 5: Keep `Program.cs` unchanged unless the tests reveal a real server-side inconsistency**

```csharp
string[] unsafeMethods = ["POST", "PUT", "DELETE", "PATCH"];
if (unsafeMethods.Contains(context.Request.Method, StringComparer.OrdinalIgnoreCase))
{
    var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
    await antiforgery.ValidateRequestAsync(context);
}
```

### Task 2: Make UI local development use same-origin `/api`

**Files:**
- Modify: `public/env.js`
- Check: `src/app/core/config/runtime-config.ts`
- Check: `proxy.conf.json`
- Check: `angular.json`
- Test: manual browser verification in local dev

- [ ] **Step 1: Use a same-origin local API base by either setting `/api` explicitly or removing the override**

Preferred minimal change:

```js
window.__env = {
  API_BASE_URL: '/api',
};
```

Alternative acceptable change if you want the default to speak for itself:

```js
window.__env = {
};
```

- [ ] **Step 2: Document in code review notes or commit message why absolute URLs break XSRF**

```text
Angular’s XSRF support automatically adds X-XSRF-TOKEN only for mutating same-origin requests.
When API_BASE_URL is http://localhost:5100/api in a browser served from http://localhost:4200,
requests become cross-origin and Angular skips the XSRF header.
```

- [ ] **Step 3: Verify Angular dev server is already configured to proxy `/api` to the portal**

Run: `rg -n "proxyConfig|target\": \"http://localhost:5100\"" angular.json proxy.conf.json`
Expected: matches in `angular.json` and `proxy.conf.json`

- [ ] **Step 4: Start the UI and confirm requests now target `/api/...` instead of `http://localhost:5100/api/...`**

Run: `yarn start`
Expected: browser network panel shows request URLs beginning with `/api`, with the dev server proxy forwarding them to `http://localhost:5100`

- [ ] **Step 5: Keep the interceptor focused on credentials only**

```ts
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const apiBase = getApiBaseUrl();

  if (req.url.startsWith(apiBase) || req.url.startsWith('/api')) {
    return next(req.clone({ withCredentials: true }));
  }

  return next(req);
};
```

### Task 3: Bootstrap antiforgery tokens from both UI shells

**Files:**
- Modify: `src/app/core/services/auth.service.ts`
- Modify: `src/app/layout/auth-layout.component.ts`
- Modify: `src/app/layout/main-layout.component.ts`
- Test: UI manual verification for guest and authenticated flows

- [ ] **Step 1: Add a reusable antiforgery bootstrap method to the auth service and include `withCredentials` explicitly**

```ts
bootstrapAntiforgery(): Observable<void> {
  return this.#http
    .get(`${this.#apiUrl}/antiforgery-token`, {
      withCredentials: true,
      responseType: 'text' as const,
    })
    .pipe(map(() => void 0));
}
```

- [ ] **Step 2: Replace the direct auth-layout HTTP call with a simple field-initializer bootstrap**

```ts
export class AuthLayoutComponent {
  readonly #authService = inject(AuthService);

  readonly #bootstrapAntiforgery = this.#authService.bootstrapAntiforgery().subscribe();
}
```

- [ ] **Step 3: Add the same bootstrap pattern to the authenticated shell without `ngOnInit`**

```ts
export class MainLayoutComponent {
  readonly #authService = inject(AuthService);

  readonly #bootstrapAntiforgery = this.#authService.bootstrapAntiforgery().subscribe();
}
```

- [ ] **Step 4: If the team prefers a lifecycle-safe cleanup path, switch both layouts to `takeUntilDestroyed()` instead of `OnInit`**

```ts
readonly #destroyRef = inject(DestroyRef);

constructor() {
  this.#authService
    .bootstrapAntiforgery()
    .pipe(takeUntilDestroyed(this.#destroyRef))
    .subscribe();
}
```

- [ ] **Step 5: Manually verify the cookie bootstrap sequence in both shells**

Run: open the app in a browser, visit `/auth/login`, then log in and navigate to an authenticated page.
Expected:
- guest shell requests `GET /api/auth/antiforgery-token`
- authenticated shell also requests `GET /api/auth/antiforgery-token`
- response sets `XSRF-TOKEN`
- subsequent `POST`/`PUT`/`DELETE`/`PATCH` calls include `X-XSRF-TOKEN`
- `GET` requests do not include `X-XSRF-TOKEN`

### Task 4: Add focused regression coverage for auth antiforgery flows

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Check: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`

- [ ] **Step 1: Add a test that `GET /api/auth/antiforgery-token` sets the readable `XSRF-TOKEN` cookie**

```csharp
[Fact]
public async Task AntiforgeryToken_SetsReadableXsrfCookie()
{
    var client = CreateClient();

    var response = await client.GetAsync("/api/auth/antiforgery-token", CancellationToken);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN="));
}
```

- [ ] **Step 2: Add a test that login refreshes antiforgery cookies after successful auth**

```csharp
[Fact]
public async Task Login_ValidCredentials_RefreshesXsrfCookie()
{
    var client = CreateClient();
    await RegisterAsync(client, new RegisterRequest("Token User", "token@example.com", "Password123!", "free"));
    await PostWithAntiforgeryAsync(client, "/api/auth/logout", new { });

    var response = await PostWithAntiforgeryAsync(client, "/api/auth/login", new LoginRequest("token@example.com", "Password123!"));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN="));
}
```

- [ ] **Step 3: Run the focused auth endpoint tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests"`
Expected: PASS

- [ ] **Step 4: If a test fails, adjust only the closest endpoint behavior and rerun the same filter**

```csharp
private static void SetXsrfTokenCookie(IAntiforgery antiforgery, HttpContext context)
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    context.Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
    {
        HttpOnly = false,
        SameSite = SameSiteMode.Lax,
        Secure = context.Request.IsHttps,
        Path = "/",
    });
}
```

### Task 5: Document the supported local auth/XSRF model

**Files:**
- Modify: `README.md`
- Check: `public/env.js`
- Check: `proxy.conf.json`

- [ ] **Step 1: Update the local development section to say the frontend should use `/api` locally**

```md
The Angular dev server should call the backend through `/api` and `proxy.conf.json`.
Do not use `http://localhost:5100/api` in local browser runtime config when relying on cookie auth and Angular XSRF support.
```

- [ ] **Step 2: Add a short troubleshooting note for `400 Antiforgery token validation failed` and explain the absolute-URL pitfall**

```md
If mutating requests return `400 Antiforgery token validation failed`, verify:
- `window.__env.API_BASE_URL` is `/api` or omitted so the runtime default resolves to `/api`
- the Angular dev server proxy is active
- `GET /api/auth/antiforgery-token` sets `XSRF-TOKEN`
- the browser sends `X-XSRF-TOKEN` on `POST`/`PUT`/`DELETE`/`PATCH`

Angular does not send `X-XSRF-TOKEN` for `GET` requests, and it also skips automatic XSRF headers for absolute/cross-origin API URLs.
```

- [ ] **Step 3: Run a quick docs sanity check**

Run: `rg -n "localhost:5100/api|/api|Antiforgery token validation failed|X-XSRF-TOKEN" README.md public/env.js`
Expected: the README and runtime config now consistently describe `/api` for local browser usage and the mutating-method behavior of XSRF headers.

## Final Verification

- [ ] **Step 1: Run focused portal auth tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~WiSave.Portal.Tests.Auth.AuthEndpointsTests|FullyQualifiedName~UnsafeProxyRequest_"`
Expected: PASS

- [ ] **Step 2: Run the UI lint or targeted check only if the touched files are covered by existing checks**

Run: `yarn eslint src/app/core/services/auth.service.ts src/app/layout/auth-layout.component.ts src/app/layout/main-layout.component.ts src/app/core/interceptors/auth.interceptor.ts`
Expected: PASS

- [ ] **Step 3: Manually verify the browser behavior end to end**

Run:
- start portal
- start UI
- load `/auth/login`
- log in
- trigger a mutating incomes or expenses call

Expected:
- cookies are present
- `X-XSRF-TOKEN` header is present on unsafe calls
- no `400 Antiforgery token validation failed` response occurs for valid flows

## Self-Review

- Spec coverage: the plan covers auth transport, antiforgery rejection verification, UI bootstrap, backend regression tests, and docs updates.
- Placeholder scan: no `TODO`/`TBD` placeholders remain; each task names exact files and commands.
- Type consistency: `bootstrapAntiforgery()` is introduced once in the auth service and reused consistently from both layouts.
