# Auth Stability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make login, refresh, logout, registration, and protected-route restoration behave deterministically by hardening server-side session storage, removing frontend antiforgery races, and preserving navigation intent.

**Architecture:** Keep the existing cookie/BFF model. The portal remains the source of truth for auth, permissions, and antiforgery. Fix instability at the boundaries: require an intentional ticket-store choice on the portal, make the Angular auth service own antiforgery readiness instead of layout constructors, and treat refresh/bootstrap failures differently from a confirmed `401`.

**Tech Stack:** ASP.NET Core Identity cookie auth, ASP.NET Core Antiforgery, distributed cache / Redis ticket store, Angular 21 standalone app, Angular `HttpClient`, Angular router guards, Jasmine/Karma frontend tests, xUnit backend tests.

---

## File Map

**Portal**
- Create: `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs` — verifies startup/session-store configuration choices fail or pass intentionally.
- Modify: `src/WiSave.Portal/Session/Extensions.cs` — stop silently falling back to process-local ticket storage in runtime environments where that causes auth loss.
- Create: `src/WiSave.Portal/Session/PortalSessionOptions.cs` — explicit configuration contract for auth-ticket storage behavior.
- Modify: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs` — align logout with antiforgery protection and rotate a fresh guest XSRF token after sign-out.
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs` — cover logout XSRF behavior and refreshed token issuance.
- Modify: `src/WiSave.Portal/appsettings.Development.json` — keep local development explicit about session-store expectations if needed.

**UI**
- Create: `../wisave-ui/src/app/core/services/auth.service.spec.ts` — regression coverage for antiforgery sequencing and `/me` bootstrap behavior.
- Create: `../wisave-ui/src/app/core/guards/auth.guard.spec.ts` — verifies redirect and error-route behavior with `returnUrl`.
- Modify: `../wisave-ui/src/app/core/services/auth.service.ts` — centralize antiforgery readiness, make initialization classify outcomes, and stop hiding transport errors as logout while preserving the existing public `logout(): void` API.
- Modify: `../wisave-ui/src/app/core/guards/auth.guard.ts` — preserve `returnUrl`, redirect only on confirmed unauthenticated state, and send transient bootstrap failures to a dedicated retry route.
- Modify: `../wisave-ui/src/app/core/guards/auth.guard.ts` — update `guestGuard` with the same bootstrap classification rules so logged-in users are not shown auth pages during backend outages.
- Modify: `../wisave-ui/src/app/features/auth/views/login.component.ts` — navigate to `returnUrl` after successful login and surface antiforgery/bootstrap failures clearly.
- Modify: `../wisave-ui/src/app/features/auth/views/register.component.ts` — same as login for post-registration flows.
- Modify: `../wisave-ui/src/app/layout/auth-layout.component.ts` — optional prewarm only; no correctness dependency on constructor timing.
- Modify: `../wisave-ui/src/app/layout/main-layout.component.ts` — same as auth layout.
- Create: `../wisave-ui/src/app/features/auth/views/session-unavailable.component.ts` — retry screen for transient bootstrap failures.
- Modify: `../wisave-ui/src/app/app.routes.ts` — add `session-unavailable` as a top-level unguarded route outside both guarded route trees.
- Check: `../wisave-ui/src/app/layout/sidebar.ts` — confirm no caller changes are needed after preserving `logout(): void`.
- Modify: `../wisave-ui/docs/features/auth.md` — document auth bootstrap states and `returnUrl` handling.
- Modify: `../wisave-ui/README.md` — document the local dependency on shared session storage + same-origin `/api`.

**Reference / Check-Only**
- Check: `../wisave-ui/public/env.js` — already set to same-origin `/api`; keep unchanged unless drift is found.
- Check: `../wisave-ui/src/app/core/interceptors/auth.interceptor.ts` — keep credential forwarding minimal; do not move security decisions here.
- Check: `../wisave-ui/proxy.conf.json` — already proxies `/api` to the portal.

---

### Task 1: Make Portal Session Storage an Explicit Runtime Choice

**Files:**
- Create: `src/WiSave.Portal/Session/PortalSessionOptions.cs`
- Modify: `src/WiSave.Portal/Session/Extensions.cs`
- Create: `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs`

- [ ] **Step 1: Write the failing configuration test for missing shared ticket storage**

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Portal.Session;
using Xunit;

namespace WiSave.Portal.Tests.Session;

public class SessionConfigurationTests
{
    [Fact]
    public void AddPortalSession_WithoutRedisAndWithoutExplicitFallback_Throws()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false",
                ["Redis:ConnectionString"] = "",
                ["Session:AllowInMemoryTicketStoreFallback"] = "false",
            })
            .Build();

        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPortalSession(configuration));

        Assert.Contains("Redis:ConnectionString", ex.Message);
    }
}
```

- [ ] **Step 2: Run the new test and verify it fails because the portal still silently falls back**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~SessionConfigurationTests"`
Expected: FAIL because `AddPortalSession()` currently uses `AddDistributedMemoryCache()` whenever Redis is absent.

- [ ] **Step 3: Add an explicit session-options contract**

```csharp
namespace WiSave.Portal.Session;

public sealed class PortalSessionOptions
{
    public bool AllowInMemoryTicketStoreFallback { get; set; }
}
```

- [ ] **Step 4: Update session registration to fail fast unless fallback is deliberately allowed**

```csharp
public static IServiceCollection AddPortalSession(this IServiceCollection services, IConfiguration configuration)
{
    services.Configure<PortalSessionOptions>(configuration.GetSection("Session"));

    var redisConnection = configuration["Redis:ConnectionString"];
    var allowFallback =
        configuration.GetValue<bool>("UseInMemoryDatabase") ||
        configuration.GetValue<bool>("Session:AllowInMemoryTicketStoreFallback");

    if (!string.IsNullOrWhiteSpace(redisConnection))
    {
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = redisConnection;
            options.InstanceName = "WiSave:";
        });
    }
    else if (allowFallback)
    {
        services.AddDistributedMemoryCache();
    }
    else
    {
        throw new InvalidOperationException(
            "Redis:ConnectionString is required for authentication ticket storage. " +
            "Set Session:AllowInMemoryTicketStoreFallback=true only for local single-instance development.");
    }

    services.AddSingleton<ITicketStore>(sp => new RedisTicketStore(sp.GetRequiredService<IDistributedCache>()));

    services.AddOptions<CookieAuthenticationOptions>(IdentityConstants.ApplicationScheme)
        .Configure<ITicketStore>((options, store) => options.SessionStore = store);

    return services;
}
```

- [ ] **Step 5: Add a positive test proving tests/local fallback still works when explicitly allowed**

```csharp
[Fact]
public void AddPortalSession_WithExplicitFallback_RegistersTicketStore()
{
    var configuration = new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Redis:ConnectionString"] = "",
            ["Session:AllowInMemoryTicketStoreFallback"] = "true",
        })
        .Build();

    var services = new ServiceCollection();
    services.AddPortalSession(configuration);

    using var provider = services.BuildServiceProvider();
    Assert.NotNull(provider.GetRequiredService<ITicketStore>());
}
```

- [ ] **Step 6: Run the focused session configuration tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~SessionConfigurationTests"`
Expected: PASS

- [ ] **Step 7: Commit the portal session-store hardening**

```bash
git add src/WiSave.Portal/Session/PortalSessionOptions.cs src/WiSave.Portal/Session/Extensions.cs tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs
git commit -m "fix(auth): make portal ticket store configuration explicit"
```

---

### Task 2: Make Logout and Antiforgery Behavior Symmetric on the Portal

**Files:**
- Modify: `src/WiSave.Portal/Endpoints/AuthEndpoints.cs`
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`

- [ ] **Step 1: Write the failing logout antiforgery test**

```csharp
[Fact]
public async Task Logout_WithoutAntiforgeryToken_Returns400()
{
    var client = CreateClient();
    await RegisterAsync(client, new RegisterRequest("Logout User", "logout-xsrf@example.com", "Password123!", "free"));

    var response = await client.PostAsJsonAsync("/api/auth/logout", new { }, CancellationToken);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
}
```

Use the existing cookie-backed `CreateClient()` and `RegisterAsync()` helper exactly as shown so the request is definitely authenticated and the only missing piece is the `X-XSRF-TOKEN` header. If this test returns `401` instead of `400`, stop and verify auth state propagation before changing endpoint behavior.

- [ ] **Step 2: Write the failing test that logout rotates a fresh readable XSRF token**

```csharp
[Fact]
public async Task Logout_WithAntiforgeryToken_RefreshesXsrfCookie()
{
    var client = CreateClient();
    await RegisterAsync(client, new RegisterRequest("Logout User", "logout-refresh@example.com", "Password123!", "free"));

    var response = await PostWithAntiforgeryAsync(client, "/api/auth/logout", new { });

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    Assert.Contains(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("XSRF-TOKEN="));
}
```

- [ ] **Step 3: Run the focused auth tests and verify logout is currently inconsistent**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~Logout_WithoutAntiforgeryToken|FullyQualifiedName~Logout_WithAntiforgeryToken_RefreshesXsrfCookie"`
Expected: FAIL because `/api/auth/logout` is not behind `AntiforgeryValidationFilter` and does not issue a fresh guest token.

- [ ] **Step 4: Protect logout with the same antiforgery filter and rotate a guest token after sign-out**

```csharp
group.MapPost("/logout", Logout)
    .AddEndpointFilter<AntiforgeryValidationFilter>()
    .RequireAuthorization()
    .Produces(204)
    .WithSummary("Clear session");

private static async Task<IResult> Logout(
    SignInManager<ApplicationUser> signInManager,
    IAntiforgery antiforgery,
    HttpContext context)
{
    await signInManager.SignOutAsync();
    SetXsrfTokenCookie(antiforgery, context);
    return Results.NoContent();
}
```

- [ ] **Step 5: Run the focused auth endpoint tests**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~Logout_WithoutAntiforgeryToken|FullyQualifiedName~Logout_WithAntiforgeryToken_RefreshesXsrfCookie|FullyQualifiedName~Logout_ClearsSession"`
Expected: PASS

- [ ] **Step 6: Commit the logout/XSRF consistency change**

```bash
git add src/WiSave.Portal/Endpoints/AuthEndpoints.cs tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs
git commit -m "fix(auth): align logout with antiforgery flow"
```

---

### Task 3: Move Antiforgery Readiness into the Angular Auth Service

**Files:**
- Create: `../wisave-ui/src/app/core/services/auth.service.spec.ts`
- Modify: `../wisave-ui/src/app/core/services/auth.service.ts`
- Modify: `../wisave-ui/src/app/layout/auth-layout.component.ts`
- Modify: `../wisave-ui/src/app/layout/main-layout.component.ts`

- [ ] **Step 1: Write the failing frontend test proving login waits for antiforgery bootstrap**

```ts
import { TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors, withXsrfConfiguration } from '@angular/common/http';
import { provideHttpClientTesting, HttpTestingController } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';

import { authInterceptor } from '@core/interceptors/auth.interceptor';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let service: AuthService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        provideHttpClient(withInterceptors([authInterceptor]), withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })),
        provideHttpClientTesting(),
      ],
    });

    service = TestBed.inject(AuthService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  it('waits for antiforgery bootstrap before login', () => {
    service.login({ email: 'user@example.com', password: 'Password123!' }).subscribe();

    const xsrf = httpMock.expectOne('/api/auth/antiforgery-token');
    const login = httpMock.expectNone('/api/auth/login');

    xsrf.flush('');
    httpMock.expectOne('/api/auth/login');
  });
});
```

- [ ] **Step 2: Run the focused frontend service test and verify it fails**

Run: `yarn --cwd ../wisave-ui test --watch=false --include src/app/core/services/auth.service.spec.ts`
Expected: FAIL because `login()` and `register()` currently issue POST requests immediately.

- [ ] **Step 3: Refactor `AuthService` so auth mutations depend on an internal antiforgery-ready observable**

```ts
import { catchError, map, Observable, of, shareReplay, switchMap, tap } from 'rxjs';

type AuthBootstrapResult =
  | { kind: 'authenticated'; user: IUser }
  | { kind: 'unauthenticated' }
  | { kind: 'unavailable'; status: number };

export class AuthService {
  #antiforgeryReady$: Observable<void> | null = null;

  #fetchAntiforgeryToken(): Observable<void> {
    return this.#http
      .get(`${this.#apiUrl}/antiforgery-token`, {
        withCredentials: true,
        responseType: 'text',
      })
      .pipe(map(() => void 0));
  }

  ensureAntiforgeryReady(forceRefresh = false): Observable<void> {
    if (forceRefresh || !this.#antiforgeryReady$) {
      this.#antiforgeryReady$ = this.#fetchAntiforgeryToken().pipe(shareReplay({ bufferSize: 1, refCount: false }));
    }

    return this.#antiforgeryReady$;
  }

  login(credentials: ILoginRequest): Observable<IAuthResponse> {
    return this.ensureAntiforgeryReady().pipe(
      switchMap(() => this.#http.post<IAuthResponse>(`${this.#apiUrl}/login`, credentials)),
      tap((res) => this.#user.set(res.user)),
    );
  }

  register(data: IRegisterRequest): Observable<IAuthResponse> {
    return this.ensureAntiforgeryReady().pipe(
      switchMap(() => this.#http.post<IAuthResponse>(`${this.#apiUrl}/register`, data)),
      tap((res) => this.#user.set(res.user)),
    );
  }

  #logoutRequest(): Observable<void> {
    return this.ensureAntiforgeryReady().pipe(
      switchMap(() => this.#http.post<void>(`${this.#apiUrl}/logout`, {})),
      tap(() => {
        this.#user.set(null);
        this.#antiforgeryReady$ = null;
      }),
      switchMap(() => this.ensureAntiforgeryReady(true)),
    );
  }

  logout(): void {
    this.#logoutRequest().subscribe({
      next: () => {
        void this.#router.navigate(['/auth/login']);
      },
      error: () => {
        this.#user.set(null);
        this.#antiforgeryReady$ = null;
        void this.#router.navigate(['/auth/login']);
      },
    });
  }
}
```

- [ ] **Step 4: Keep layout bootstrapping as an optional warm-up, not a correctness requirement**

```ts
export class AuthLayoutComponent {
  readonly #authService = inject(AuthService);

  constructor() {
    this.#authService.ensureAntiforgeryReady().subscribe({ error: () => void 0 });
  }
}
```

```ts
export class MainLayoutComponent {
  readonly #authService = inject(AuthService);

  constructor() {
    this.#authService.ensureAntiforgeryReady().subscribe({ error: () => void 0 });
  }
}
```

- [ ] **Step 5: Add a second service test proving logout refreshes antiforgery readiness**

```ts
it('refreshes antiforgery after logout completes', () => {
  service.logout();

  httpMock.expectOne('/api/auth/antiforgery-token').flush('');
  httpMock.expectOne('/api/auth/logout').flush({});
  httpMock.expectOne('/api/auth/antiforgery-token').flush('');
});
```

Assert this through the existing public `logout(): void` API. Do not test `#private` helpers directly.

- [ ] **Step 6: Run the focused frontend auth service tests**

Run: `yarn --cwd ../wisave-ui test --watch=false --include src/app/core/services/auth.service.spec.ts`
Expected: PASS

- [ ] **Step 7: Commit the frontend antiforgery sequencing refactor**

```bash
git -C ../wisave-ui add src/app/core/services/auth.service.ts src/app/core/services/auth.service.spec.ts src/app/layout/auth-layout.component.ts src/app/layout/main-layout.component.ts
git -C ../wisave-ui commit -m "fix(auth): serialize antiforgery bootstrap in auth service"
```

---

### Task 4: Distinguish Real Logout from Session Bootstrap Failure and Preserve Return URL

**Files:**
- Create: `../wisave-ui/src/app/core/guards/auth.guard.spec.ts`
- Modify: `../wisave-ui/src/app/core/services/auth.service.ts`
- Modify: `../wisave-ui/src/app/core/guards/auth.guard.ts`
- Modify: `../wisave-ui/src/app/features/auth/views/login.component.ts`
- Modify: `../wisave-ui/src/app/features/auth/views/register.component.ts`
- Create: `../wisave-ui/src/app/features/auth/views/session-unavailable.component.ts`
- Modify: `../wisave-ui/src/app/app.routes.ts`

- [ ] **Step 1: Write the failing guard test for `returnUrl` preservation**

```ts
it('redirects unauthenticated users to login with returnUrl', async () => {
  const result = await firstValueFrom(
    authGuard({} as never, { url: '/expenses/budgets' } as never) as Observable<UrlTree>,
  );

  expect(router.serializeUrl(result)).toBe('/auth/login?returnUrl=%2Fexpenses%2Fbudgets');
});
```

Add a paired failing test for the guest flow:

```ts
it('redirects guest-guard bootstrap failures to the unguarded session-unavailable route', async () => {
  const result = await firstValueFrom(
    guestGuard({} as never, { url: '/auth/login' } as never) as Observable<UrlTree>,
  );

  expect(router.serializeUrl(result)).toBe('/session-unavailable?returnUrl=%2Fauth%2Flogin');
});
```

- [ ] **Step 2: Write the failing service test that treats non-401 `/me` failures as unavailable, not logged out**

```ts
it('classifies 500 from /me as unavailable', () => {
  let result: AuthBootstrapResult | undefined;

  service.initialize().subscribe((value) => {
    result = value;
  });

  httpMock.expectOne('/api/auth/me').flush('boom', { status: 500, statusText: 'Server Error' });

  expect(result).toEqual({ kind: 'unavailable', status: 500 });
  expect(service.isInitialized()).toBeFalse();
});
```

- [ ] **Step 3: Run the focused guard/service tests and verify they fail**

Run: `yarn --cwd ../wisave-ui test --watch=false --include src/app/core/services/auth.service.spec.ts --include src/app/core/guards/auth.guard.spec.ts`
Expected: FAIL because `initialize()` currently converts every error into logged-out state and guards discard the original destination.

- [ ] **Step 4: Refactor `initialize()` to return an explicit bootstrap result**

```ts
initialize(): Observable<AuthBootstrapResult> {
  return this.#http.get<IUser>(`${this.#apiUrl}/me`).pipe(
    map((user) => ({ kind: 'authenticated', user }) as const),
    tap(({ user }) => {
      this.#user.set(user);
      this.#initialized.set(true);
    }),
    catchError((err: HttpErrorResponse) => {
      if (err.status === 401) {
        this.#user.set(null);
        this.#initialized.set(true);
        return of({ kind: 'unauthenticated' } as const);
      }

      this.#initialized.set(false);
      return of({ kind: 'unavailable', status: err.status } as const);
    }),
  );
}
```

- [ ] **Step 5: Update guards to preserve destination and route transient failures to a retry page**

```ts
export const authGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isInitialized()) {
    return authService.isAuthenticated()
      ? true
      : router.createUrlTree(['/auth/login'], { queryParams: { returnUrl: state.url } });
  }

  return authService.initialize().pipe(
    map((result) => {
      if (result.kind === 'authenticated') return true;
      if (result.kind === 'unauthenticated') {
        return router.createUrlTree(['/auth/login'], { queryParams: { returnUrl: state.url } });
      }

      return router.createUrlTree(['/session-unavailable'], { queryParams: { returnUrl: state.url } });
    }),
  );
};

export const guestGuard: CanActivateFn = (_route, state) => {
  const authService = inject(AuthService);
  const router = inject(Router);

  if (authService.isInitialized()) {
    return authService.isAuthenticated()
      ? router.createUrlTree(['/incomes'])
      : true;
  }

  return authService.initialize().pipe(
    map((result) => {
      if (result.kind === 'authenticated') {
        return router.createUrlTree(['/incomes']);
      }
      if (result.kind === 'unauthenticated') {
        return true;
      }

      return router.createUrlTree(['/session-unavailable'], { queryParams: { returnUrl: state.url } });
    }),
  );
};
```

- [ ] **Step 6: Add a minimal retry screen that keeps the original destination**

```ts
@Component({
  selector: 'app-session-unavailable',
  template: `
    <section class="mx-auto flex min-h-screen max-w-xl flex-col items-center justify-center gap-4 px-6 text-center">
      <h1 class="text-2xl font-bold">We couldn't restore your session</h1>
      <p class="text-secondary-600">The server did not confirm whether you are signed in. Retry before logging in again.</p>
      <button pButton type="button" label="Retry" severity="secondary" (click)="retry()" />
      <a routerLink="/auth/login" [queryParams]="{ returnUrl: returnUrl() }">Go to sign in</a>
    </section>
  `,
})
export class SessionUnavailableComponent {
  readonly #route = inject(ActivatedRoute);
  readonly #router = inject(Router);

  readonly returnUrl = signal(this.#route.snapshot.queryParamMap.get('returnUrl') ?? '/incomes');

  retry(): void {
    void this.#router.navigateByUrl(this.returnUrl());
  }
}
```

- [ ] **Step 7: Mount the retry screen as an unguarded top-level route**

```ts
export const routes: Routes = [
  {
    path: 'session-unavailable',
    loadComponent: () =>
      import('./features/auth/views/session-unavailable.component').then((m) => m.SessionUnavailableComponent),
  },
  {
    path: 'auth',
    loadComponent: () => import('./layout/auth-layout.component').then((m) => m.AuthLayoutComponent),
    canActivate: [guestGuard],
    loadChildren: () => import('./features/auth/auth.routes').then((m) => m.routes),
  },
  {
    path: '',
    loadComponent: () => import('./layout/main-layout.component').then((m) => m.MainLayoutComponent),
    canActivate: [authGuard],
    loadChildren: () => import('./features/features.routing').then((m) => m.routes),
  },
];
```

- [ ] **Step 8: Update login and register success paths to respect `returnUrl`**

```ts
readonly #route = inject(ActivatedRoute);

onLogin(credentials: { email: string; password: string }): void {
  this.isLoading.set(true);
  this.error.set(null);

  this.#authService.login(credentials).subscribe({
    next: () => {
      this.isLoading.set(false);
      const returnUrl = this.#route.snapshot.queryParamMap.get('returnUrl') ?? '/incomes';
      void this.#router.navigateByUrl(returnUrl);
    },
    error: (err: HttpErrorResponse) => {
      this.isLoading.set(false);
      this.error.set(err.status === 400
        ? 'Security validation expired. Please try again.'
        : err.status === 401
          ? 'Invalid email or password.'
          : 'Login failed. Please try again.');
    },
  });
}
```

- [ ] **Step 9: Run the focused auth guard and service tests**

Run: `yarn --cwd ../wisave-ui test --watch=false --include src/app/core/services/auth.service.spec.ts --include src/app/core/guards/auth.guard.spec.ts`
Expected: PASS

- [ ] **Step 10: Commit the auth-bootstrap and navigation fix**

```bash
git -C ../wisave-ui add src/app/core/services/auth.service.ts src/app/core/guards/auth.guard.ts src/app/core/guards/auth.guard.spec.ts src/app/features/auth/views/login.component.ts src/app/features/auth/views/register.component.ts src/app/features/auth/views/session-unavailable.component.ts src/app/app.routes.ts
git -C ../wisave-ui commit -m "fix(auth): preserve return paths and separate auth loss from bootstrap failure"
```

---

### Task 5: Regression Coverage, Docs, and Full Verification

**Files:**
- Modify: `tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs`
- Modify: `tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs`
- Modify: `../wisave-ui/docs/features/auth.md`
- Modify: `../wisave-ui/README.md`

- [ ] **Step 1: Re-run the focused backend auth/session slice**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj --filter "FullyQualifiedName~AuthEndpointsTests|FullyQualifiedName~UserHeaderTransformTests|FullyQualifiedName~SessionConfigurationTests"`
Expected: PASS

- [ ] **Step 2: Document the actual auth model and failure modes**

```md
## Auth Bootstrap

- The browser talks only to `/api`
- The portal owns cookies, session state, and antiforgery
- `GET /api/auth/antiforgery-token` is a bootstrap dependency for guest and authenticated shells
- `GET /api/auth/me` has three outcomes:
  - authenticated
  - unauthenticated (`401`)
  - unavailable (transport/server failure; do not treat as logout)

## Local Development

- Run the portal with Redis unless `Session:AllowInMemoryTicketStoreFallback=true` is set intentionally
- In-memory ticket storage is for tests or single-process local debugging only
```

- [ ] **Step 3: Run frontend tests and lint**

Run: `yarn --cwd ../wisave-ui test --watch=false`
Expected: PASS

Run: `yarn --cwd ../wisave-ui lint`
Expected: PASS

- [ ] **Step 4: Run the complete portal test suite**

Run: `dotnet test`
Expected: PASS

- [ ] **Step 5: Manual smoke test the exact scenarios the user reported**

Run:
1. `docker compose up -d postgres redis`
2. `dotnet run --project src/WiSave.Portal`
3. `yarn --cwd ../wisave-ui start`

Manual checks:
1. Open `http://localhost:4200/auth/login` and submit immediately after page load.
Expected: login succeeds; no intermittent `400 Antiforgery token validation failed`.

2. Register a fresh account from `http://localhost:4200/auth/register`.
Expected: registration succeeds; app lands on the originally intended route or `/incomes`.

3. Navigate to a protected deep link such as `http://localhost:4200/expenses/budgets`, refresh the page, and observe bootstrap.
Expected: user remains on the same page when `/me` returns `200`; no redirect to `/auth/login`.

4. Stop Redis temporarily or force a `/me` server failure.
Expected: app shows the session-unavailable retry screen instead of pretending the user is logged out.

5. Log out, then immediately log back in or register another account.
Expected: guest flow already has a fresh XSRF token; no stuck auth form.

- [ ] **Step 6: Commit docs and verification fallout**

```bash
git add tests/WiSave.Portal.Tests/Auth/AuthEndpointsTests.cs tests/WiSave.Portal.Tests/Session/SessionConfigurationTests.cs
git -C ../wisave-ui add README.md docs/features/auth.md
git commit -m "test(auth): add auth stability regression coverage"
git -C ../wisave-ui commit -m "docs(auth): document session bootstrap and local requirements"
```

---

## Self-Review

**Spec coverage:** The plan covers the confirmed root causes from the review:
- silent process-local session fallback on the portal
- frontend XSRF/bootstrap race
- treating non-401 `/me` failures as logout
- loss of original destination after forced login
- logout/XSRF inconsistency

**Placeholder scan:** No `TODO`, `TBD`, or “handle appropriately” placeholders remain. Each task names concrete files, commands, and intended code.

**Type consistency:** `AuthBootstrapResult`, `ensureAntiforgeryReady()`, `SessionUnavailableComponent`, and `PortalSessionOptions` are referenced consistently across tasks.
