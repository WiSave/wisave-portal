# Portal Multi-Bus Design

## Goal

Add full MassTransit multi-bus support to `WiSave.Portal` so the application can register and use multiple typed buses explicitly, and so integration tests exercise the same typed bus topology shape as production.

## Current State

- `src/WiSave.Portal/Messaging/Extensions.cs` registers a single typed bus via `AddMassTransit<IExpensesBus>(...)`.
- `src/WiSave.Portal.Contracts/Bus/IPortalBus.cs` already exists as a second bus marker, but it is not wired into the application.
- Integration tests in `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs` add `AddMassTransitTestHarness()` as a default harness, which does not match the production typed-bus registration.
- This mismatch makes tests ambiguous: the app consumes through a typed bus, while the tests publish through the default harness bus.

## Requirements

### Functional

1. The portal must support registering more than one typed MassTransit bus.
2. Each bus must have explicit ownership via its marker interface.
3. Non-consumer application code must be able to publish and send through a specific bus explicitly.
4. Integration tests must publish through the same typed bus shape used by the application.
5. Existing notification consumer behavior must continue to work on the expenses bus.

### Non-Functional

1. Keep the change aligned with existing extension-based service registration patterns.
2. Keep the public shape simple: one registration method for messaging, focused helper types where needed, and no broad refactor outside the messaging/test boundary.
3. Prefer explicitness over convention magic. Bus selection should be obvious in DI.

## Recommended Approach

Use true MassTransit multi-bus registration.

- Register one bus per marker interface with `AddMassTransit<TBus>()`.
- Keep expenses events on `IExpensesBus`.
- Introduce full runtime wiring for `IPortalBus`, even if it does not immediately host consumers.
- Use `Bind<TBus, IPublishEndpoint>` and `Bind<TBus, ISendEndpointProvider>` in non-consumer code so bus selection remains explicit.
- In integration tests, replace the default harness registration with test DI that mirrors the typed bus being exercised.

This is preferred over a test-only workaround because it keeps production and test configuration aligned and makes future bus additions straightforward.

## Architecture

### Messaging Registration

`src/WiSave.Portal/Messaging/Extensions.cs` will become the single entry point for multi-bus registration.

- Read a bus-specific configuration section for each bus.
- Register `IExpensesBus` with the current `NotificationConsumer` endpoint configuration.
- Register `IPortalBus` with its own host settings and endpoint configuration.
- Keep endpoint naming deterministic and consistent with current formatter usage.

### Bus-Specific Publish/Send Access

Where application code needs to publish or send outside a consumer scope, it should resolve the bus-bound endpoint via `Bind<TBus, ...>`.

This avoids relying on the default `IPublishEndpoint` or `ISendEndpointProvider`, which is ambiguous in multi-bus setups outside consume scopes.

### Test Wiring

Integration tests that currently rely on `ITestHarness` plus the default bus registration will be updated to target the typed bus explicitly.

The key point is not to bolt a separate default harness onto the container and assume it drives the typed bus consumers. Test registration must mirror the production bus identity:

- the same bus marker interface,
- the same consumers under test,
- an in-memory transport for deterministic testing.

## Configuration Shape

The messaging configuration should support one section per bus. The simplest maintainable shape is:

```json
{
  "RabbitMq": {
    "Expenses": {
      "Host": "localhost",
      "VirtualHost": "expenses",
      "Username": "guest",
      "Password": "guest"
    },
    "Portal": {
      "Host": "localhost",
      "VirtualHost": "portal",
      "Username": "guest",
      "Password": "guest"
    }
  }
}
```

Backward compatibility may be preserved for the existing flat settings by treating them as the default for `Expenses` when the nested section is absent.

## File-Level Design

### `src/WiSave.Portal/Messaging/Extensions.cs`

- Expand from single-bus setup to multi-bus registration.
- Extract repeated broker-setting reads into a small private helper record or method if repetition becomes noisy.
- Keep `NotificationConsumer` attached to the expenses bus only.

### `src/WiSave.Portal.Contracts/Bus/IPortalBus.cs`

- Keep as the portal bus marker.
- No functional changes required unless namespace or XML docs need cleanup.

### Additional Messaging Helper File

If the application does not already have a clean place for explicit bus publishing, add a focused helper file under `src/WiSave.Portal/Messaging/` for bus-bound publisher abstractions or helper methods.

This file should exist only if needed by current usage. Do not add wrappers without a concrete call site.

### `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- Replace default harness assumptions with typed-bus-aware test setup.
- Publish through the expenses bus registration used by the app under test.
- Keep test intent unchanged: validate SignalR notifications from consumed events.

## Error Handling

- Missing bus-specific configuration should fall back to safe defaults where that already exists today for the expenses bus.
- The portal bus should fail in a normal, visible MassTransit startup way if explicitly configured incorrectly; no custom error layer is needed.
- Tests should fail clearly when the typed bus is not registered, rather than silently publishing on an unrelated default bus.

## Testing Strategy

### Integration Tests

Primary validation should focus on the nearest relevant tests:

- `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

These tests should verify that messages published through the expenses typed bus are consumed and forwarded to SignalR clients.

### Broader Verification

After targeted integration tests pass, run:

- `dotnet build`
- targeted integration tests for messaging
- broader `dotnet test` if the local environment allows it

## Risks

1. MassTransit test harness APIs around typed buses are easier to misconfigure than the default single-bus harness.
2. Accidentally leaving code on plain `IPublishEndpoint` outside consumers would make bus routing ambiguous.
3. Changing configuration shape without a backward-compatibility path could break existing local environments.

## Mitigations

1. Keep typed bus ownership explicit in DI and in tests.
2. Preserve current expenses defaults while adding nested per-bus configuration.
3. Limit the first implementation to the expenses and portal buses only.

## Success Criteria

1. `WiSave.Portal` registers both `IExpensesBus` and `IPortalBus`.
2. Expenses notifications still reach SignalR clients.
3. Messaging tests publish through the typed expenses bus path rather than a separate default test bus.
4. The configuration and DI setup make bus ownership obvious to future contributors.
