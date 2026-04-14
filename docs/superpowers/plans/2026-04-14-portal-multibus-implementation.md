# Portal Multi-Bus Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full MassTransit multi-bus support to `WiSave.Portal` and align integration tests so they publish through the typed expenses bus path instead of a separate default harness bus.

**Architecture:** Keep messaging registration centralized in `src/WiSave.Portal/Messaging/Extensions.cs`, add explicit configuration and registration for `IExpensesBus` and `IPortalBus`, and keep bus ownership explicit through `Bind<TBus, ...>` for any non-consumer publishing surface. Update the messaging integration tests to mirror the typed bus registration shape used by the app, using in-memory transport for test determinism.

**Tech Stack:** ASP.NET Core, MassTransit, RabbitMQ, xUnit v3, WebApplicationFactory

---

### Task 1: Write The Failing Multi-Bus Messaging Tests

**Files:**
- Modify: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`
- Test: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Replace the default test harness dependency with typed-bus access in the test file**

Update the test setup so it no longer assumes `ITestHarness` is the correct publishing surface. Add the portal bus contract namespace and prepare the test to resolve a typed publish endpoint.

```csharp
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using WiSave.Expenses.Contracts.Bus;
using WiSave.Portal.Contracts.Bus;
```

Replace direct `ITestHarness` usage sites with a helper call that will resolve a bus-bound publish endpoint:

```csharp
await PublishOnExpensesBus(new ExpenseRecorded(
    ExpenseId: "exp-1",
    UserId: userId,
    AccountId: "acc-1",
    CategoryId: "cat-1",
    SubcategoryId: null,
    Amount: 99.99m,
    Currency: Currency.PLN,
    Date: new DateOnly(2026, 4, 1),
    Description: "Test expense",
    Recurring: false,
    Metadata: null,
    Timestamp: DateTimeOffset.UtcNow
));
```

Add the helper skeleton at the bottom of the file:

```csharp
private async Task PublishOnExpensesBus<T>(T message)
    where T : class
{
    using var scope = _factory.Services.CreateScope();
    var publishEndpoint = scope.ServiceProvider
        .GetRequiredService<Bind<IExpensesBus, IPublishEndpoint>>();

    await publishEndpoint.Value.Publish(message, CancellationToken);
}
```

- [ ] **Step 2: Add an explicit failing test that proves the typed expenses bus must be registered**

Add this test near the top of the class:

```csharp
[Fact]
public void Services_ExposeTypedExpensesBusPublishEndpoint()
{
    using var scope = _factory.Services.CreateScope();

    var publishEndpoint = scope.ServiceProvider.GetService<Bind<IExpensesBus, IPublishEndpoint>>();
    var portalBus = scope.ServiceProvider.GetService<IBusInstance<IPortalBus>>();

    Assert.NotNull(publishEndpoint);
    Assert.NotNull(portalBus);
}
```

This test should fail until the app registers both typed buses and exposes the bound publish endpoint for the expenses bus.

- [ ] **Step 3: Run the targeted test to verify RED**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests.Services_ExposeTypedExpensesBusPublishEndpoint"
```

Expected:
- FAIL
- The failure should indicate that `Bind<IExpensesBus, IPublishEndpoint>` and/or `IBusInstance<IPortalBus>` is not available from the container.

- [ ] **Step 4: Run the existing messaging tests to capture the current mismatch**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests"
```

Expected:
- FAIL
- Existing messaging tests should still fail because the test host publishes on the wrong bus shape and the auth/antiforgery path is currently unstable.

- [ ] **Step 5: Commit the red tests**

```bash
git add tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs
git commit -m "test: define typed bus expectations for messaging integration tests"
```

### Task 2: Implement Runtime Multi-Bus Registration

**Files:**
- Modify: `src/WiSave.Portal/Messaging/Extensions.cs`
- Modify: `src/WiSave.Portal.Contracts/Bus/IPortalBus.cs` (only if XML docs or namespace cleanup is needed)
- Test: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Expand messaging configuration to support per-bus settings while preserving current defaults**

Replace the current flat setting reads in `src/WiSave.Portal/Messaging/Extensions.cs` with a helper record and loader:

```csharp
using MassTransit;
using WiSave.Expenses.Contracts.Bus;
using WiSave.Portal.Contracts.Bus;

namespace WiSave.Portal.Messaging;

public static class Extensions
{
    public static IServiceCollection AddPortalMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var expensesSettings = GetBusSettings(configuration, "Expenses", "expenses");
        var portalSettings = GetBusSettings(configuration, "Portal", "portal");

        services.AddMassTransit<IExpensesBus>(x =>
        {
            x.AddConsumer<NotificationConsumer>();
            x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
            x.UsingRabbitMq((context, cfg) =>
            {
                ConfigureRabbitMqHost(cfg, expensesSettings);
                cfg.ConfigureEndpoints(context);
            });
        });

        services.AddMassTransit<IPortalBus>(x =>
        {
            x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
            x.UsingRabbitMq((context, cfg) =>
            {
                ConfigureRabbitMqHost(cfg, portalSettings);
                cfg.ConfigureEndpoints(context);
            });
        });

        return services;
    }

    private static RabbitMqBusSettings GetBusSettings(
        IConfiguration configuration,
        string sectionName,
        string defaultVirtualHost)
    {
        var section = configuration.GetSection($"RabbitMq:{sectionName}");

        return new RabbitMqBusSettings(
            Host: section["Host"] ?? configuration["RabbitMq:Host"] ?? "localhost",
            VirtualHost: section["VirtualHost"] ?? configuration["RabbitMq:VirtualHost"] ?? defaultVirtualHost,
            Username: section["Username"] ?? configuration["RabbitMq:Username"] ?? "guest",
            Password: section["Password"] ?? configuration["RabbitMq:Password"] ?? "guest"
        );
    }

    private static void ConfigureRabbitMqHost(
        IRabbitMqBusFactoryConfigurator cfg,
        RabbitMqBusSettings settings)
    {
        cfg.Host(settings.Host, settings.VirtualHost, h =>
        {
            h.Username(settings.Username);
            h.Password(settings.Password);
        });
    }

    private sealed record RabbitMqBusSettings(
        string Host,
        string VirtualHost,
        string Username,
        string Password);
}
```

- [ ] **Step 2: Run the focused container test to verify the minimal implementation turns GREEN**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests.Services_ExposeTypedExpensesBusPublishEndpoint"
```

Expected:
- PASS
- `IExpensesBus` publish binding and `IPortalBus` bus instance should now resolve from DI.

- [ ] **Step 3: Refactor only if needed to keep the registration readable**

If `Extensions.cs` is still noisy after the helper extraction, keep the file as-is unless there is clear duplication. Do not introduce additional abstractions beyond the settings helper record and host helper above.

- [ ] **Step 4: Run the messaging integration tests again to see the remaining failures clearly**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests"
```

Expected:
- Some failures may remain.
- The failure mode should shift away from missing typed bus registrations and toward either test-host transport wiring or the pre-existing auth/antiforgery issue.

- [ ] **Step 5: Commit the runtime multi-bus registration**

```bash
git add src/WiSave.Portal/Messaging/Extensions.cs src/WiSave.Portal.Contracts/Bus/IPortalBus.cs tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs
git commit -m "feat: register portal multibus messaging"
```

### Task 3: Align Integration Tests With The Typed Expenses Bus

**Files:**
- Modify: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`
- Test: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Replace the default MassTransit test harness registration with typed-bus-aware in-memory registration**

Inside the `WithWebHostBuilder` test setup, remove the default harness call and replace it with MassTransit registrations that mirror production bus identities:

```csharp
builder.ConfigureServices(services =>
{
    services.AddMassTransit<IExpensesBus>(x =>
    {
        x.AddConsumer<NotificationConsumer>();
        x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context);
        });
    });

    services.AddMassTransit<IPortalBus>(x =>
    {
        x.SetEndpointNameFormatter(new DefaultEndpointNameFormatter(".", null, true));
        x.UsingInMemory((context, cfg) =>
        {
            cfg.ConfigureEndpoints(context);
        });
    });
});
```

If the existing app registration causes duplicate bus registration conflicts in the test host, remove the production MassTransit registrations from the service collection first, then add the in-memory typed-bus registrations back in the same builder block.

- [ ] **Step 2: Update the message-publishing tests to use the helper consistently**

Change all three event tests to publish through the helper rather than through `ITestHarness`:

```csharp
await PublishOnExpensesBus(new CommandFailed(
    CorrelationId: correlationId,
    UserId: userId,
    CommandType: "RecordExpense",
    Reason: "Insufficient funds",
    Timestamp: DateTimeOffset.UtcNow
));
```

Remove the now-unused `MassTransit.Testing` dependency from the file if nothing else uses it.

- [ ] **Step 3: Run the messaging integration tests to verify GREEN**

Run:

```bash
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests"
```

Expected:
- PASS for the typed-bus exposure test.
- The three SignalR messaging tests should pass if the remaining auth setup is healthy.
- If auth/antiforgery failures remain, capture them explicitly and do not claim full green until resolved.

- [ ] **Step 4: Run broader verification**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging"
```

Expected:
- `dotnet build` passes.
- Messaging-focused integration tests pass, or any remaining failures are isolated and documented with exact test names.

- [ ] **Step 5: Commit the test-host alignment**

```bash
git add tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs
git commit -m "test: align messaging integration tests with typed buses"
```

### Task 4: Final Verification And Handoff

**Files:**
- Review only: `src/WiSave.Portal/Messaging/Extensions.cs`
- Review only: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Run final verification commands**

Run:

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~WiSave.Portal.IntegrationTests.Messaging.ConsumerSignalRTests"
```

Expected:
- Build succeeds.
- Messaging integration tests pass, or remaining failures are captured exactly.

- [ ] **Step 2: Review the diff for scope control**

Run:

```bash
git diff --stat HEAD~3..HEAD
git diff -- src/WiSave.Portal/Messaging/Extensions.cs tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs
```

Expected:
- Only messaging registration and targeted messaging tests should have changed for this feature.

- [ ] **Step 3: Prepare the handoff summary**

The final handoff must state:

```text
- what changed in runtime multi-bus registration
- what changed in typed-bus integration test wiring
- verification commands run and their actual results
- any remaining auth/antiforgery failures, if still present
```
