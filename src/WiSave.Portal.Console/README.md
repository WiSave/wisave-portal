# WiSave.Portal.Console

Operator-focused console shell for portal maintenance commands.

## Run

Interactive shell:

```bash
dotnet run --project src/WiSave.Portal.Console
```

Direct command execution:

```bash
dotnet run --project src/WiSave.Portal.Console -- db-migrate
dotnet run --project src/WiSave.Portal.Console -- db-migrate --connection-string "Host=localhost;Database=wisave_portal;Username=wisave;Password=wisave_dev"
dotnet run --project src/WiSave.Portal.Console -- db-seed
dotnet run --project src/WiSave.Portal.Console -- db-seed --connection-string "Host=localhost;Database=wisave_portal;Username=wisave;Password=wisave_dev"
```

## Add a new command

1. Create a class in `Commands/` that implements `IPortalCommand`.
2. Define command metadata through `Name`, `Description`, and `ParameterDefinitions`.
3. Put operational logic behind a service in `Operations/`.
4. Inject the operation service into the command.

Commands are discovered automatically from the assembly and exposed in both interactive mode and direct CLI mode.
