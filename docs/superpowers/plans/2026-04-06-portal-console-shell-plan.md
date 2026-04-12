# WiSave Portal Console Shell Plan

## Context

`WiSave.Portal` is currently the main ASP.NET Core host for authentication, authorization, SignalR, messaging, and YARP proxying. The solution also contains `WiSave.Portal.Migrations`, which already exposes a small console-style entry point over reusable migration logic. The current solution file does not yet include a dedicated console shell application.

The goal is to introduce `WiSave.Portal.Console` as a non-Dockerized operator tool that:

- runs locally as a standalone console application
- lets the user choose commands interactively
- accepts parameters for commands
- executes one command at a time
- returns to the command menu after each command completes
- makes it easy to add new commands and reuse the same execution model

The user asked to keep the design simple and not split the operational logic into a separate `WiSave.Portal.Operations` project. Because of that, the console solution should stay as a single project while still preserving internal separation of concerns through folders, namespaces, and interfaces.

## Architectural Direction

Create a new project:

- `src/WiSave.Portal.Console`

Add it to:

- `WiSave.Portal.slnx`

Do not add it to:

- `docker-compose.yml`

The console project should use a generic host so it can reuse standard .NET patterns for:

- configuration
- dependency injection
- logging
- scoped command execution

Inside the single project, keep the code organized into these internal areas:

- `Shell`
  - interactive loop
  - menu rendering
  - follow-up prompt after command completion
- `Commands`
  - one class per command
  - command metadata and validation
- `Execution`
  - command catalog
  - parser
  - prompt flow
  - runner
- `Operations`
  - actual database, identity, and migration work
- `Infrastructure`
  - registrations, configuration binding, shared setup

This keeps the solution small without collapsing the implementation into `Program.cs`.

## Core Design

Use a metadata-driven command model so new commands can be added by registering a new class instead of editing a central switch statement.

Suggested contracts:

```csharp
public interface IPortalCommand
{
    string Name { get; }
    string Description { get; }
    IReadOnlyList<CommandParameter> Parameters { get; }
    Task<CommandResult> ExecuteAsync(CommandExecutionContext context, CancellationToken ct);
}
```

```csharp
public sealed record CommandParameter(
    string Name,
    string Description,
    bool Required,
    string? DefaultValue = null);
```

```csharp
public sealed record CommandResult(
    bool Success,
    string Message,
    IReadOnlyList<string>? Details = null);
```

Use supporting services:

- `ICommandCatalog`
  - discovers registered commands
  - resolves a command by name
- `ICommandParser`
  - parses direct CLI arguments into command name and parameter values
- `ICommandPrompter`
  - interactively asks for missing values
- `ICommandRunner`
  - creates a scope and executes the chosen command
- `IConsoleShell`
  - drives the repeated choose-execute-repeat workflow

Commands should stay thin. Each command should depend on one or more operation services rather than directly owning EF Core or Identity logic.

## Execution Modes

The same command definitions should support both modes:

### Interactive mode

Triggered when no command-line arguments are provided.

Flow:

1. Start host and resolve command catalog.
2. Display available commands.
3. Let the user choose a command by number or name.
4. Prompt for required parameters.
5. Execute the command.
6. Print the result.
7. Ask whether to run another command.
8. Return to the command list until the user exits.

### Direct mode

Triggered when command-line arguments are provided.

Example:

```bash
dotnet run --project src/WiSave.Portal.Console -- db-migrate
dotnet run --project src/WiSave.Portal.Console -- users-create --email admin@wisave.local --name Admin --plan premium
```

This mode is useful for scripted or repeatable operator workflows while still reusing the same command implementations.

## Recommended First Commands

Start with a small set that validates the pattern:

1. `db-migrate`
   - wraps existing `DbMigrator`
   - reuses `ConnectionStrings__Portal`
2. `users-create`
   - creates a portal user
   - assigns a plan
   - optionally assigns a role
3. `users-set-plan`
   - changes an existing user plan
4. `plans-list`
   - lists available plans

These commands are enough to prove the shell, prompting, validation, and service structure.

## Configuration Strategy

The console should follow the same configuration conventions already used in the portal:

- `appsettings.json`
- `appsettings.Development.json`
- environment variables
- `ConnectionStrings__Portal`

This avoids introducing a second configuration model. The console should be runnable locally against the same Postgres instance as the portal without requiring Docker Compose changes.

If configuration needs diverge later, add a dedicated `appsettings.json` inside `WiSave.Portal.Console`, but keep connection naming aligned with the portal.

## Implementation Steps

1. Create `src/WiSave.Portal.Console` targeting `net10.0`.
2. Add the project to `WiSave.Portal.slnx`.
3. Set up a generic host in `Program.cs` with configuration, logging, and DI.
4. Add the internal folder structure: `Shell`, `Commands`, `Execution`, `Operations`, `Infrastructure`.
5. Implement the core command contracts and the command catalog.
6. Implement direct CLI parsing for `command-name --param value`.
7. Implement the interactive shell loop with repeated execution.
8. Add a command prompter for missing required parameters.
9. Add a command runner that creates a DI scope per execution.
10. Implement `db-migrate` as the first command using existing migration logic.
11. Implement one user-management command to validate DB and Identity access patterns.
12. Add a short README or docs entry with usage examples.

## Constraints and Guardrails

- Do not add `WiSave.Portal.Console` to `docker-compose.yml`.
- Do not place business logic in `Program.cs`.
- Do not use a large switch statement for commands.
- Do not couple interactive prompting logic to specific commands.
- Keep commands small and delegate real work to services.
- Prefer registration through DI so new commands can be added with minimal friction.

## Risks

### Risk: console project references too much web-only code

If the console directly depends on web host concerns, it will become harder to maintain and test.

Mitigation:

- reference only the pieces actually needed
- move reusable database or identity setup behind console-local services where necessary
- keep web middleware and HTTP concerns out of the console project

### Risk: command implementations become inconsistent

If each command invents its own parameter and prompt logic, the shell will become uneven.

Mitigation:

- standardize metadata via `CommandParameter`
- keep prompting and validation in shared runner services

### Risk: interactive mode becomes hard to automate

If the shell is the only entry mode, the tool will be inconvenient for scripts.

Mitigation:

- support both interactive mode and direct CLI mode from the start

## Summary

The recommended approach is to add a single new project, `WiSave.Portal.Console`, and keep it out of Docker Compose. The project should provide an interactive shell and direct CLI execution using a shared command model, DI registration, and scoped execution. Operational logic should stay in the same project but live behind services so commands remain thin and easy to add.

This approach keeps the solution simple, avoids unnecessary project sprawl, and creates a maintainable path for adding future portal administration commands.
