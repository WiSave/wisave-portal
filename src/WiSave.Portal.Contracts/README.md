# WiSave.Portal.Contracts

`WiSave.Portal.Contracts` contains shared transport and integration contracts owned by `WiSave.Portal`.

It is intended for downstream services and other internal consumers that need a stable contract for:

- forwarded user context headers
- shared permission constants
- portal integration abstractions

## Versioning

The package is published automatically from the `master` branch.

Version format:

- project `VersionPrefix`: `0.1`
- published package version: `0.1.<github_run_number>`

## Example

```xml
<PackageReference Include="WiSave.Portal.Contracts" Version="0.1.123" />
```

```csharp
using WiSave.Portal.Contracts.Authorization;
using WiSave.Portal.Contracts.Identity;

var headers = ForwardedUserContextWriter.Write(
    new ForwardedUserContext(
        "user-1",
        "user@example.com",
        new HashSet<string> { PortalPermissions.Expenses.Read },
        new HashSet<string>()));
```
