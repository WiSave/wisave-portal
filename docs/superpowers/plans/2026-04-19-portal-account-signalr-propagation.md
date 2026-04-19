# Portal Account SignalR Propagation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `wisave-portal` emit explicit FE-facing full account snapshots for `account.opened` and `account.updated`, then make `wisave-ui` consume those snapshots by replacing account entities instead of merging partial updates.

**Architecture:** Keep the existing `RealtimeEnvelope` and existing `NotificationConsumer`, but carve out an explicit account-specific adapter path inside the portal. On the UI side, unify `account.opened` and `account.updated` around one full-snapshot payload shape and one mapper, then remove shape-sensitive merge logic from the account SignalR feature.

**Tech Stack:** ASP.NET Core SignalR, MassTransit, xUnit, Angular 21, NGRX Signals, Vitest, Yarn 4

**Spec:** `docs/superpowers/specs/2026-04-19-portal-account-signalr-propagation-design.md`

**Working tree note:** Both `wisave-portal` and `wisave-ui` already contain unrelated local changes. Every commit in this plan is path-limited on purpose. Do not use broad `git add .` or broad repo-wide commits while executing it.

**Precondition:** `wisave-portal` must reference a `WiSave.Expenses.Contracts` package version that already includes `AccountOpened` and `AccountUpdated` with `Variant`, `PreviousCycleDebt`, and `CurrentCycleDebt`. If that package is not yet published or not yet consumed by `src/WiSave.Portal/WiSave.Portal.csproj`, stop and resolve that dependency first before starting implementation.

---

### File Map

| File | Action | Responsibility |
| ---- | ------ | -------------- |
| `src/WiSave.Portal/Hubs/Realtime/ExpensesAccountRealtimePayload.cs` | Create | FE-facing full snapshot DTO for account realtime events |
| `src/WiSave.Portal/WiSave.Portal.csproj` | Verify | Confirm the consumed `WiSave.Expenses.Contracts` package version contains the new account event shape before starting code changes |
| `src/WiSave.Portal/Messaging/NotificationConsumer.cs` | Modify | Explicit portal-side mapping and push path for `AccountOpened` / `AccountUpdated` |
| `tests/WiSave.Portal.UnitTests/Messaging/NotificationConsumerEnvelopeTests.cs` | Modify | Prove account envelopes carry FE-facing mapped payloads and full snapshots |
| `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs` | Modify | Verify real SignalR clients receive full account snapshots from portal |
| `../wisave-ui/src/app/core/signalr/expenses-signalr.types.ts` | Modify | Make `IAccountUpdatedPayload` a full snapshot shape instead of `Partial<>` |
| `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.ts` | Modify | Remove account patch merge logic and replace updates via the full mapper |
| `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts` | Create | Lock full-snapshot mapping and replacement semantics for account realtime |

---

### Task 1: Add Portal Account Realtime DTO And Explicit Mapping

**Files:**
- Create: `src/WiSave.Portal/Hubs/Realtime/ExpensesAccountRealtimePayload.cs`
- Modify: `src/WiSave.Portal/Messaging/NotificationConsumer.cs`
- Test: `tests/WiSave.Portal.UnitTests/Messaging/NotificationConsumerEnvelopeTests.cs`

- [ ] **Step 1: Write failing portal unit tests for FE-facing account payloads**

Update `tests/WiSave.Portal.UnitTests/Messaging/NotificationConsumerEnvelopeTests.cs` by replacing the current account assertion with two explicit tests:

```csharp
[Fact]
public async Task AccountOpened_sent_as_full_account_snapshot_payload()
{
    var (hub, _, group) = CreateHub();
    var consumer = new NotificationConsumer(hub);

    var userId = Guid.NewGuid().ToString();
    var accountId = Guid.NewGuid().ToString();

    var msg = new AccountOpened(
        AccountId: accountId,
        UserId: userId,
        Name: "Millennium",
        Type: AccountType.CreditCard,
        Variant: null,
        Currency: Currency.PLN,
        Balance: null,
        LinkedBankAccountId: "bank-1",
        CreditLimit: 5000m,
        BillingCycleDay: 16,
        PreviousCycleDebt: 1200m,
        CurrentCycleDebt: 340m,
        Color: "#f59e0b",
        LastFourDigits: "4532",
        Timestamp: DateTimeOffset.UtcNow);

    var ctx = Substitute.For<ConsumeContext<AccountOpened>>();
    ctx.Message.Returns(msg);
    ctx.CancellationToken.Returns(CancellationToken.None);

    await consumer.Consume(ctx);

    var env = CaptureSentEnvelope(group);
    Assert.Equal(RealtimeEventType.AccountOpened, env.EventType);
    Assert.Equal(accountId, env.EntityId);

    var payload = Assert.IsType<ExpensesAccountRealtimePayload>(env.Payload);
    Assert.Equal("CreditCard", payload.Type);
    Assert.Null(payload.Variant);
    Assert.Null(payload.Balance);
    Assert.Equal("bank-1", payload.LinkedBankAccountId);
    Assert.Equal(1200m, payload.PreviousCycleDebt);
    Assert.Equal(340m, payload.CurrentCycleDebt);
}

[Fact]
public async Task AccountUpdated_sent_as_full_account_snapshot_payload_not_patch()
{
    var (hub, _, group) = CreateHub();
    var consumer = new NotificationConsumer(hub);

    var userId = Guid.NewGuid().ToString();
    var accountId = Guid.NewGuid().ToString();

    var msg = new AccountUpdated(
        AccountId: accountId,
        UserId: userId,
        Name: "Travel Card",
        Type: AccountType.DebitCard,
        Variant: DebitCardVariant.Standalone,
        Currency: Currency.EUR,
        Balance: 250m,
        LinkedBankAccountId: null,
        CreditLimit: null,
        BillingCycleDay: null,
        PreviousCycleDebt: null,
        CurrentCycleDebt: null,
        Color: null,
        LastFourDigits: "8812",
        Timestamp: DateTimeOffset.UtcNow);

    var ctx = Substitute.For<ConsumeContext<AccountUpdated>>();
    ctx.Message.Returns(msg);
    ctx.CancellationToken.Returns(CancellationToken.None);

    await consumer.Consume(ctx);

    var env = CaptureSentEnvelope(group);
    Assert.Equal(RealtimeEventType.AccountUpdated, env.EventType);
    Assert.Equal(accountId, env.EntityId);

    var payload = Assert.IsType<ExpensesAccountRealtimePayload>(env.Payload);
    Assert.Equal("DebitCard", payload.Type);
    Assert.Equal("standalone", payload.Variant);
    Assert.Equal(250m, payload.Balance);
    Assert.Null(payload.LinkedBankAccountId);
}
```

- [ ] **Step 2: Run the portal unit tests to verify the DTO and mapper are missing**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --filter "FullyQualifiedName~NotificationConsumerEnvelopeTests"
```

Expected:

- FAIL because `ExpensesAccountRealtimePayload` does not exist
- FAIL because `NotificationConsumer` still forwards raw `AccountOpened` / `AccountUpdated` payloads

- [ ] **Step 3: Add the FE-facing account realtime DTO**

Create `src/WiSave.Portal/Hubs/Realtime/ExpensesAccountRealtimePayload.cs`:

```csharp
namespace WiSave.Portal.Hubs.Realtime;

public sealed record ExpensesAccountRealtimePayload(
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

- [ ] **Step 4: Implement explicit account mapping inside `NotificationConsumer`**

Update `src/WiSave.Portal/Messaging/NotificationConsumer.cs` so account events use explicit DTO mapping while expense and budget events stay on the generic `Push(...)` path:

```csharp
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using WiSave.Expenses.Contracts.Events;
using WiSave.Expenses.Contracts.Events.Accounts;
using WiSave.Expenses.Contracts.Events.Budgets;
using WiSave.Expenses.Contracts.Events.Expenses;
using WiSave.Portal.Hubs;
using WiSave.Portal.Hubs.Realtime;

namespace WiSave.Portal.Messaging;

public class NotificationConsumer(IHubContext<NotificationsHub> hub) :
    IConsumer<AccountOpened>,
    IConsumer<AccountUpdated>,
    IConsumer<AccountClosed>,
    IConsumer<ExpenseRecorded>,
    IConsumer<ExpenseUpdated>,
    IConsumer<ExpenseDeleted>,
    IConsumer<BudgetCreated>,
    IConsumer<BudgetCopiedFromPrevious>,
    IConsumer<OverallLimitSet>,
    IConsumer<CategoryLimitSet>,
    IConsumer<CategoryLimitRemoved>,
    IConsumer<CommandFailed>
{
    public Task Consume(ConsumeContext<AccountOpened> ctx) =>
        PushAccount(ctx, RealtimeEventType.AccountOpened, MapAccountPayload(ctx.Message));

    public Task Consume(ConsumeContext<AccountUpdated> ctx) =>
        PushAccount(ctx, RealtimeEventType.AccountUpdated, MapAccountPayload(ctx.Message));

    public Task Consume(ConsumeContext<AccountClosed> ctx) =>
        Push(ctx, RealtimeEventType.AccountClosed, ctx.Message.UserId, ctx.Message.AccountId);

    public Task Consume(ConsumeContext<ExpenseRecorded> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseRecorded, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<ExpenseUpdated> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseUpdated, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<ExpenseDeleted> ctx) =>
        Push(ctx, RealtimeEventType.ExpenseDeleted, ctx.Message.UserId, ctx.Message.ExpenseId);

    public Task Consume(ConsumeContext<BudgetCreated> ctx) =>
        Push(ctx, RealtimeEventType.BudgetCreated, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<BudgetCopiedFromPrevious> ctx) =>
        Push(ctx, RealtimeEventType.BudgetCopiedFromPrevious, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<OverallLimitSet> ctx) =>
        Push(ctx, RealtimeEventType.OverallLimitSet, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CategoryLimitSet> ctx) =>
        Push(ctx, RealtimeEventType.CategoryLimitSet, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CategoryLimitRemoved> ctx) =>
        Push(ctx, RealtimeEventType.CategoryLimitRemoved, ctx.Message.UserId, ctx.Message.BudgetId);

    public Task Consume(ConsumeContext<CommandFailed> ctx) =>
        Push(ctx, RealtimeEventType.CommandFailed, ctx.Message.UserId, entityId: null);

    private Task PushAccount<T>(
        ConsumeContext<T> ctx,
        string eventType,
        ExpensesAccountRealtimePayload payload)
        where T : class
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: payload.AccountId,
            Payload: payload);

        return hub.Clients.Group(payload.UserId).SendAsync("realtimeEvent", env, ctx.CancellationToken);
    }

    private static ExpensesAccountRealtimePayload MapAccountPayload(AccountOpened message) =>
        new(
            message.AccountId,
            message.UserId,
            message.Name,
            message.Type.ToString(),
            message.Variant switch
            {
                DebitCardVariant.Linked => "linked",
                DebitCardVariant.Standalone => "standalone",
                _ => null,
            },
            message.Currency.ToString(),
            message.Balance,
            message.LinkedBankAccountId,
            message.CreditLimit,
            message.BillingCycleDay,
            message.PreviousCycleDebt,
            message.CurrentCycleDebt,
            message.Color,
            message.LastFourDigits,
            message.Timestamp);

    private static ExpensesAccountRealtimePayload MapAccountPayload(AccountUpdated message) =>
        new(
            message.AccountId,
            message.UserId,
            message.Name,
            message.Type.ToString(),
            message.Variant switch
            {
                DebitCardVariant.Linked => "linked",
                DebitCardVariant.Standalone => "standalone",
                _ => null,
            },
            message.Currency.ToString(),
            message.Balance,
            message.LinkedBankAccountId,
            message.CreditLimit,
            message.BillingCycleDay,
            message.PreviousCycleDebt,
            message.CurrentCycleDebt,
            message.Color,
            message.LastFourDigits,
            message.Timestamp);

    private Task Push<T>(ConsumeContext<T> ctx, string eventType, string userId, string? entityId)
        where T : class
    {
        var env = new RealtimeEnvelope(
            EventId: Guid.CreateVersion7(),
            Domain: "expenses",
            EventType: eventType,
            OccurredAt: DateTime.UtcNow,
            EntityId: entityId,
            Payload: ctx.Message!);

        return hub.Clients.Group(userId).SendAsync("realtimeEvent", env, ctx.CancellationToken);
    }
}
```

- [ ] **Step 5: Run the portal unit tests again**

Run:

```bash
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --filter "FullyQualifiedName~NotificationConsumerEnvelopeTests"
```

Expected:

- PASS

- [ ] **Step 6: Commit the portal DTO and consumer changes**

Use a path-limited commit:

```bash
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal add \
  src/WiSave.Portal/Hubs/Realtime/ExpensesAccountRealtimePayload.cs \
  src/WiSave.Portal/Messaging/NotificationConsumer.cs \
  tests/WiSave.Portal.UnitTests/Messaging/NotificationConsumerEnvelopeTests.cs

git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal commit -m "feat(portal): emit explicit account signalr payloads"
```

---

### Task 2: Prove The Portal Boundary With SignalR Integration Tests

**Files:**
- Modify: `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`

- [ ] **Step 1: Write failing SignalR integration assertions for opened and updated full snapshots**

Extend `tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs`:

1. Update the existing `AccountOpened_IsPushedToSignalRClient` test so it publishes the new account event shape and asserts FE-facing payload values:

```csharp
[Fact]
public async Task AccountOpened_IsPushedToSignalRClient()
{
    var (connection, userId) = await CreateAuthenticatedHubConnection("account@example.com");

    var tcs = new TaskCompletionSource<JsonElement>();
    connection.On<JsonElement>("realtimeEvent", envelope =>
    {
        if (envelope.GetProperty("eventType").GetString() == "account.opened")
            tcs.TrySetResult(envelope);
    });

    await connection.StartAsync(CancellationToken);

    await PublishOnExpensesBus(new AccountOpened(
        AccountId: "acc-42",
        UserId: userId,
        Name: "Main Account",
        Type: AccountType.CreditCard,
        Variant: null,
        Currency: Currency.PLN,
        Balance: null,
        LinkedBankAccountId: "bank-1",
        CreditLimit: 5000m,
        BillingCycleDay: 16,
        PreviousCycleDebt: 1200m,
        CurrentCycleDebt: 340m,
        Color: "#FF0000",
        LastFourDigits: "1234",
        Timestamp: DateTimeOffset.UtcNow
    ));

    var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
    var payload = envelope.GetProperty("payload");
    Assert.Equal("account.opened", envelope.GetProperty("eventType").GetString());
    Assert.Equal("CreditCard", payload.GetProperty("type").GetString());
    Assert.True(payload.GetProperty("variant").ValueKind == JsonValueKind.Null);
    Assert.Equal(1200m, payload.GetProperty("previousCycleDebt").GetDecimal());
    Assert.Equal(340m, payload.GetProperty("currentCycleDebt").GetDecimal());
    Assert.Equal("bank-1", payload.GetProperty("linkedBankAccountId").GetString());

    await connection.StopAsync(CancellationToken);
    await connection.DisposeAsync();
}
```

2. Add a new `AccountUpdated_IsPushedToSignalRClient_AsFullSnapshot` test:

```csharp
[Fact]
public async Task AccountUpdated_IsPushedToSignalRClient_AsFullSnapshot()
{
    var (connection, userId) = await CreateAuthenticatedHubConnection("account-update@example.com");

    var tcs = new TaskCompletionSource<JsonElement>();
    connection.On<JsonElement>("realtimeEvent", envelope =>
    {
        if (envelope.GetProperty("eventType").GetString() == "account.updated")
            tcs.TrySetResult(envelope);
    });

    await connection.StartAsync(CancellationToken);

    await PublishOnExpensesBus(new AccountUpdated(
        AccountId: "acc-77",
        UserId: userId,
        Name: "Travel Card",
        Type: AccountType.DebitCard,
        Variant: DebitCardVariant.Standalone,
        Currency: Currency.EUR,
        Balance: 250m,
        LinkedBankAccountId: null,
        CreditLimit: null,
        BillingCycleDay: null,
        PreviousCycleDebt: null,
        CurrentCycleDebt: null,
        Color: null,
        LastFourDigits: "8812",
        Timestamp: DateTimeOffset.UtcNow
    ));

    var envelope = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5), CancellationToken);
    var payload = envelope.GetProperty("payload");
    Assert.Equal("account.updated", envelope.GetProperty("eventType").GetString());
    Assert.Equal("DebitCard", payload.GetProperty("type").GetString());
    Assert.Equal("standalone", payload.GetProperty("variant").GetString());
    Assert.Equal(250m, payload.GetProperty("balance").GetDecimal());
    Assert.True(payload.GetProperty("linkedBankAccountId").ValueKind == JsonValueKind.Null);

    await connection.StopAsync(CancellationToken);
    await connection.DisposeAsync();
}
```

- [ ] **Step 2: Run the portal integration tests to verify the boundary is still raw/generic**

Run:

```bash
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj --filter "FullyQualifiedName~ConsumerSignalRTests"
```

Expected:

- FAIL before the consumer mapping is implemented
- or FAIL before the new account event constructors are updated

- [ ] **Step 3: Make the integration test constructors and assertions match the explicit snapshot boundary**

After Task 1 code is in place, ensure both account event constructor calls in `ConsumerSignalRTests.cs` include:

```csharp
Variant: null,
PreviousCycleDebt: ...,
CurrentCycleDebt: ...,
```

and the assertions check FE-facing strings in the SignalR payload rather than raw enum numbers.

- [ ] **Step 4: Run the portal integration tests again**

Run:

```bash
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj --filter "FullyQualifiedName~ConsumerSignalRTests"
```

Expected:

- PASS

- [ ] **Step 5: Commit the portal integration boundary tests**

```bash
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal add \
  tests/WiSave.Portal.IntegrationTests/Messaging/ConsumerSignalRTests.cs

git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal commit -m "test(portal): verify account signalr snapshot payloads"
```

---

### Task 3: Switch UI Account SignalR Handling To Full Snapshot Replacement

**Files:**
- Modify: `../wisave-ui/src/app/core/signalr/expenses-signalr.types.ts`
- Modify: `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.ts`
- Create: `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts`

- [ ] **Step 1: Write failing UI tests for full account snapshot updates**

Create `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts`:

```ts
import { Currency } from '@core/types/currency.enum';
import type { IAccountOpenedPayload, IAccountUpdatedPayload } from '@core/signalr/expenses-signalr.types';
import { mapAccountFromSignalR } from './accounts.signalr.event-handlers';

describe('accounts.signalr.event-handlers', () => {
  it('maps debit card update payloads as full standalone snapshots', () => {
    const payload: IAccountUpdatedPayload = {
      accountId: 'card-1',
      userId: 'user-1',
      name: 'Travel Card',
      type: 'DebitCard',
      variant: 'standalone',
      currency: 'EUR',
      balance: 250,
      linkedBankAccountId: null,
      creditLimit: null,
      billingCycleDay: null,
      previousCycleDebt: null,
      currentCycleDebt: null,
      color: null,
      lastFourDigits: '8812',
      timestamp: '2026-04-19T10:00:00Z',
    };

    const account = mapAccountFromSignalR(payload as IAccountOpenedPayload);

    expect(account).toEqual({
      id: 'card-1',
      name: 'Travel Card',
      type: 'debit_card',
      variant: 'standalone',
      currency: Currency.EUR,
      balance: 250,
      lastFourDigits: '8812',
    });
  });

  it('maps credit card update payloads as full snapshots with both debt buckets', () => {
    const payload: IAccountUpdatedPayload = {
      accountId: 'card-2',
      userId: 'user-1',
      name: 'Millennium',
      type: 'CreditCard',
      variant: null,
      currency: 'PLN',
      balance: null,
      linkedBankAccountId: 'bank-1',
      creditLimit: 5000,
      billingCycleDay: 16,
      previousCycleDebt: 1200,
      currentCycleDebt: 340,
      color: '#f59e0b',
      lastFourDigits: '4532',
      timestamp: '2026-04-19T10:00:00Z',
    };

    const account = mapAccountFromSignalR(payload as IAccountOpenedPayload);

    expect(account).toEqual({
      id: 'card-2',
      name: 'Millennium',
      type: 'credit_card',
      currency: Currency.PLN,
      originAccountUid: 'bank-1',
      creditLimit: 5000,
      billingCycleDay: 16,
      previousCycleDebt: 1200,
      currentCycleDebt: 340,
      color: '#f59e0b',
      lastFourDigits: '4532',
    });
  });
});
```

- [ ] **Step 2: Run the focused UI tests to verify account updates are still patch-shaped**

Run:

```bash
cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui
yarn test --watch=false --include src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts
```

Expected:

- FAIL because no dedicated spec exists yet
- or FAIL because `IAccountUpdatedPayload` is still `Partial<IAccountOpenedPayload>`

- [ ] **Step 3: Make account update payloads full snapshots**

Update `../wisave-ui/src/app/core/signalr/expenses-signalr.types.ts`:

```ts
export type IAccountUpdatedPayload = IAccountOpenedPayload;
```

Replace:

```ts
export interface IAccountUpdatedPayload extends Partial<IAccountOpenedPayload> {
  accountId: string;
  userId: string;
  timestamp: string;
}
```

with the single full-snapshot alias above.

- [ ] **Step 4: Remove account patch merge logic and replace via the full mapper**

Update `../wisave-ui/src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.ts`:

1. Delete the entire `mergeAccountUpdate(...)` function.
2. Remove the now-unused `mapAccountType(...)` helper.
3. Simplify `accountUpdated$` to use `mapAccountFromSignalR(...)` directly.

The relevant code should become:

```ts
import { inject } from '@angular/core';
import { toObservable } from '@angular/core/rxjs-interop';
import { signalStoreFeature, withProps } from '@ngrx/signals';
import { withEventHandlers } from '@ngrx/signals/events';
import { filter, map, pairwise } from 'rxjs';

import { Currency } from '@core/types/currency.enum';
import type { ExpenseAccountType, ExpenseAccountTypeApi, IExpenseAccount } from '@core/types/expense-account.interface';
import { asExpenseAccountId } from '@core/types/expense-id.types';

import { ExpensesSignalRService } from '@core/signalr/expenses-signalr.service';
import { PortalSignalRService } from '@core/signalr/portal-signalr.service';
import type { IAccountOpenedPayload, IAccountUpdatedPayload } from '@core/signalr/expenses-signalr.types';

import { accountsPageEvents, accountsSignalREvents } from './accounts.events';

const ACCOUNT_TYPE_MAP: Record<ExpenseAccountTypeApi, ExpenseAccountType> = {
  BankAccount: 'bank_account',
  DebitCard: 'debit_card',
  CreditCard: 'credit_card',
  Cash: 'cash',
};

function mapCurrency(value: string | null | undefined): Currency {
  if (!value) return Currency.PLN;
  return (Object.values(Currency) as string[]).includes(value) ? (value as Currency) : Currency.PLN;
}

export function mapAccountFromSignalR(payload: IAccountOpenedPayload): IExpenseAccount {
  const id = asExpenseAccountId(payload.accountId);
  const name = payload.name;
  const currency = mapCurrency(payload.currency);
  const color = payload.color ?? undefined;
  const lastFourDigits = payload.lastFourDigits ?? undefined;

  switch (payload.type) {
    case 'BankAccount':
      return {
        id,
        name,
        type: 'bank_account',
        currency,
        balance: payload.balance ?? 0,
        ...(color && { color }),
      };
    case 'Cash':
      return {
        id,
        name,
        type: 'cash',
        currency,
        balance: payload.balance ?? 0,
        ...(color && { color }),
      };
    case 'DebitCard':
      if (payload.variant === 'linked') {
        return {
          id,
          name,
          type: 'debit_card',
          variant: 'linked',
          currency,
          originAccountUid: asExpenseAccountId(payload.linkedBankAccountId ?? ''),
          ...(color && { color }),
          ...(lastFourDigits && { lastFourDigits }),
        };
      }

      return {
        id,
        name,
        type: 'debit_card',
        variant: 'standalone',
        currency,
        balance: payload.balance ?? 0,
        ...(color && { color }),
        ...(lastFourDigits && { lastFourDigits }),
      };
    case 'CreditCard':
      return {
        id,
        name,
        type: 'credit_card',
        currency,
        originAccountUid: asExpenseAccountId(payload.linkedBankAccountId ?? ''),
        creditLimit: payload.creditLimit ?? 0,
        billingCycleDay: payload.billingCycleDay ?? 1,
        previousCycleDebt: payload.previousCycleDebt ?? 0,
        currentCycleDebt: payload.currentCycleDebt ?? 0,
        ...(color && { color }),
        ...(lastFourDigits && { lastFourDigits }),
      };
  }
}

export function withAccountsSignalR() {
  return signalStoreFeature(
    withProps(() => ({
      _realtime: inject(ExpensesSignalRService),
      _portal: inject(PortalSignalRService),
    })),
    withEventHandlers((store) => ({
      accountOpened$: store._realtime.accountOpened$.pipe(
        filter((env) => env.entityId !== null && env.payload !== null),
        map((env) => accountsSignalREvents.accountUpsertedSignalR({
          account: mapAccountFromSignalR(env.payload as IAccountOpenedPayload),
        })),
      ),
      accountUpdated$: store._realtime.accountUpdated$.pipe(
        filter((env) => env.entityId !== null && env.payload !== null),
        map((env) => accountsSignalREvents.accountUpsertedSignalR({
          account: mapAccountFromSignalR(env.payload as IAccountUpdatedPayload),
        })),
      ),
      accountClosed$: store._realtime.accountClosed$.pipe(
        filter((env) => env.entityId !== null),
        map((env) => accountsSignalREvents.accountRemovedSignalR({
          id: asExpenseAccountId(env.entityId as string),
        })),
      ),
      reconnectCatchUp$: toObservable(store._portal.status).pipe(
        pairwise(),
        filter(([prev, curr]) => (prev === 'reconnecting' || prev === 'disconnected') && curr === 'connected'),
        map(() => accountsPageEvents.opened()),
      ),
    })),
  );
}
```

- [ ] **Step 5: Run the focused UI tests again**

Run:

```bash
cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui
yarn test --watch=false --include src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts
```

Expected:

- PASS

- [ ] **Step 6: Commit the UI full-snapshot account SignalR changes**

```bash
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui add \
  src/app/core/signalr/expenses-signalr.types.ts \
  src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.ts \
  src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts

git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui commit -m "feat(ui): replace account signalr entities from full snapshots"
```

---

### Task 4: Run Cross-Repo Verification

**Files:**
- Modify: none

- [ ] **Step 1: Run focused portal tests**

Run:

```bash
cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal
dotnet test tests/WiSave.Portal.UnitTests/WiSave.Portal.UnitTests.csproj --filter "FullyQualifiedName~NotificationConsumerEnvelopeTests"
dotnet test tests/WiSave.Portal.IntegrationTests/WiSave.Portal.IntegrationTests.csproj --filter "FullyQualifiedName~ConsumerSignalRTests"
```

Expected:

- both commands PASS

- [ ] **Step 2: Run focused UI tests**

Run:

```bash
cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui
yarn test --watch=false --include src/app/features/expense-accounts/+store/accounts/accounts.signalr.event-handlers.spec.ts
```

Expected:

- PASS

- [ ] **Step 3: Run broader portal and UI build checks**

Run:

```bash
cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal
dotnet build

cd /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui
yarn build
```

Expected:

- both builds PASS

- [ ] **Step 4: Inspect both working trees before handoff**

Run:

```bash
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal status --short
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-portal diff --stat

git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui status --short
git -C /Users/jakubchwastek/Desktop/Projects/wisave_project/wisave-ui diff --stat
```

Expected:

- only the intended portal account realtime files and UI account SignalR files are part of this feature’s changes
- any pre-existing unrelated local changes remain untouched

---

### Self-Review

- Spec coverage: the plan covers the portal contracts-package precondition, portal-side explicit account DTO mapping, portal SignalR boundary tests, UI full-snapshot account payload typing, removal of account patch merge logic, and cross-repo verification.
- Placeholder scan: no `TODO`, `TBD`, or “implement later” steps remain.
- Type consistency: `ExpensesAccountRealtimePayload`, `IAccountUpdatedPayload`, `AccountOpened`, `AccountUpdated`, and the full-snapshot replacement semantics are consistent across portal and UI tasks.
