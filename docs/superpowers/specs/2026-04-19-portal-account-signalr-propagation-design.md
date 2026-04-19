# Portal Account SignalR Propagation Design

## Goal

Adjust the `wisave-portal` -> `wisave-ui` realtime boundary for expenses account events so the frontend receives explicit, FE-oriented full account snapshots instead of raw expenses contracts.

The scope of this change is limited to:

- `account.opened`
- `account.updated`
- `account.closed`

Expense and budget realtime events stay on the current generic pass-through path for now.

## Context

Current flow:

1. `wisave-expenses` publishes MassTransit events such as `AccountOpened` and `AccountUpdated`.
2. `wisave-portal` consumes those events in `NotificationConsumer`.
3. The portal wraps the raw message in a generic `RealtimeEnvelope`.
4. `wisave-ui` filters portal SignalR envelopes by `domain === 'expenses'` and interprets the payload as a frontend account event.

Current frontend behavior for accounts is fragile:

- `account.updated` is treated like a partial patch.
- The store merges updates into the existing entity shape.
- Type and debit-card variant transitions are risky because the merge logic depends on the current local shape.
- The portal boundary does not explicitly control enum/string shape for frontend-facing account payloads.

This is no longer a good fit after the account model changed to discriminated account variants and credit-card debt buckets.

## Problem

The realtime boundary is too generic for account events.

The frontend now depends on:

- full account-kind shape
- explicit debit-card variant
- nullable `balance`
- `previousCycleDebt`
- `currentCycleDebt`

If account realtime stays generic and patch-oriented:

- `linked` -> `standalone` debit-card changes can be merged incorrectly
- cross-type transitions rely on brittle FE logic
- the portal has no explicit contract for what the UI is supposed to receive
- enum serialization remains an implicit transport detail instead of a controlled boundary

## Recommended Approach

Use an explicit, non-generic portal-side adapter for account realtime events.

For account events only:

1. `wisave-expenses` remains the source of domain events.
2. `wisave-portal` maps `AccountOpened` and `AccountUpdated` to a dedicated FE-facing realtime payload.
3. `wisave-portal` pushes that payload inside the existing `RealtimeEnvelope`.
4. `wisave-ui` treats both `account.opened` and `account.updated` as full snapshots and replaces the account entity wholesale.
5. `account.closed` remains id-based and removes the entity.

This keeps full control over:

- payload shape
- enum/string values
- nullability
- what the FE may rely on as a complete account snapshot

## Rejected Approaches

### 1. Keep generic raw-contract forwarding

Rejected because the FE still has to interpret domain contracts directly and keep shape-sensitive merge logic.

### 2. Fetch the account from expenses on every account update

Rejected because the portal can receive the event before the expenses read model is caught up. Immediate refreshes can return stale state and introduce extra coupling, latency, and failure modes.

### 3. Hydrate only when fields are missing

Rejected because it keeps two code paths:

- direct payload usage
- refresh/recompute fallback

That adds complexity without solving the core issue that account updates should already be full snapshots.

## Portal Realtime DTO

Add an explicit FE-facing account realtime payload used only by the portal realtime layer.

Recommended shape for both `account.opened` and `account.updated`:

```csharp
public sealed record AccountPayload(
    string AccountId,
    string UserId,
    string Name,
    string Type,
    string? Variant,
    string Currency,
    decimal? Balance,
    string? LinkedBankAccountId,
    decimal? CreditLimit,
    int? BillingCycleDay,
    decimal? PreviousCycleDebt,
    decimal? CurrentCycleDebt,
    string? Color,
    string? LastFourDigits,
    DateTimeOffset Timestamp);
```

Notes:

- `Type` is FE-facing string output such as `BankAccount`, `DebitCard`, `CreditCard`, `Cash`
- `Variant` is FE-facing string output such as `linked`, `standalone`, or `null`
- `Balance` stays nullable
- both debt buckets stay nullable for non-credit-card accounts
- this DTO is a portal adapter contract, not a domain event contract

## Portal Mapping Rules

The portal should explicitly map account events instead of forwarding the raw message object.

### `AccountOpened`

Map the event to `AccountPayload` and publish it as:

- `domain: "expenses"`
- `eventType: "account.opened"`
- `entityId: message.AccountId`
- `payload: mapped full snapshot`

### `AccountUpdated`

Map the event to the same `AccountPayload` shape and publish it as:

- `domain: "expenses"`
- `eventType: "account.updated"`
- `entityId: message.AccountId`
- `payload: mapped full snapshot`

This is intentionally not a patch payload. The frontend should receive a complete account snapshot every time.

### `AccountClosed`

Keep the current lightweight event:

- `domain: "expenses"`
- `eventType: "account.closed"`
- `entityId: message.AccountId`
- `payload: raw close payload is acceptable`

No special DTO is required for close at this stage.

## Implementation Shape In Portal

Do not redesign the whole notification pipeline. Keep the existing `NotificationConsumer`, but make account propagation explicit inside it.

Recommended implementation:

- keep generic `Push(...)` for expense and budget events
- add account-specific push paths for `AccountOpened` and `AccountUpdated`
- add a small private mapper or a nearby static mapper dedicated to account realtime payloads

Example direction:

```csharp
public Task Consume(ConsumeContext<AccountOpened> ctx) =>
    PushAccountOpened(ctx);

public Task Consume(ConsumeContext<AccountUpdated> ctx) =>
    PushAccountUpdated(ctx);
```

with:

- `MapAccountRealtimePayload(AccountOpened message)`
- `MapAccountRealtimePayload(AccountUpdated message)`

No generic “map every expenses contract to FE DTO” abstraction should be introduced in this change.

## Frontend Consumption Rules

The frontend should stop treating `account.updated` as a partial patch.

### UI SignalR Types

In `wisave-ui`, `IAccountUpdatedPayload` should become the same full snapshot shape as `IAccountOpenedPayload`.

That means:

- remove `Partial<IAccountOpenedPayload>` semantics for account updates
- keep one FE assumption: opened and updated carry full account state

### UI Store Handling

For account realtime:

- `account.opened` -> map payload -> upsert/replace
- `account.updated` -> map payload -> upsert/replace
- `account.closed` -> remove entity

The account store should not merge partial updates for account events anymore.

### UI Derived Data

The frontend may still compute presentation-only derived values locally, for example:

- settlement date labels
- effective-balance breakdown
- due/pending presentation

But it should not reconstruct missing domain state from partial account update payloads.

## Reconnect Strategy

Keep the existing reconnect catch-up in the UI as a safety net:

- after disconnect/reconnect, the accounts page may still trigger an HTTP resync

But after this change, reconnect catch-up becomes a backup consistency path, not the normal mechanism required to repair incomplete realtime account payloads.

## Testing

### Portal

Add or update tests so the portal proves it emits FE-facing account payloads:

- `AccountOpened` SignalR envelope contains:
  - `eventType = "account.opened"`
  - full payload
  - string `type`
  - string `variant`
  - `previousCycleDebt`
  - `currentCycleDebt`
- `AccountUpdated` SignalR envelope contains:
  - `eventType = "account.updated"`
  - full payload, not patch semantics

The existing integration path in `ConsumerSignalRTests` is the right place for boundary verification.

### UI

Update or add tests so the FE proves account updates replace entities safely:

- linked debit -> standalone debit update replaces shape correctly
- standalone debit -> linked debit update replaces shape correctly
- bank account -> credit card update replaces shape correctly
- account update path no longer depends on partial merge logic

## File Impact

### Portal

- `src/WiSave.Portal/Messaging/NotificationConsumer.cs`
- create a small account realtime DTO file near the portal realtime/messaging code
- `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

### UI

- `src/app/core/signalr/expenses-signalr.types.ts`
- `src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.ts`
- related FE specs for account SignalR handling

## Out Of Scope

- expense realtime payload redesign
- budget realtime payload redesign
- replacing the existing `RealtimeEnvelope`
- generic portal-side DTO mapping for all downstream services
- portal-side HTTP hydration from expenses on account updates

## Result

After this change:

- account realtime becomes explicit and frontend-oriented
- portal owns the account SignalR contract intentionally
- UI account updates become full-snapshot replacement, not patch merging
- type and debit-card variant transitions stop depending on brittle FE merge logic
