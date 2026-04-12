# WiSave.Portal.Contracts GitHub Actions Design

Date: 2026-04-12

## Summary

Add a GitHub Actions workflow that builds, tests, packs, and publishes `WiSave.Portal.Contracts` to GitHub Packages whenever code is merged into `master`.

The package version will be generated automatically from a fixed `VersionPrefix` in the project and the GitHub Actions run number:

- project `VersionPrefix`: for example `0.1`
- published package version: `0.1.<github_run_number>`

This produces a stable, monotonically increasing internal package version on every merge to `master`.

## Goals

- publish `WiSave.Portal.Contracts` automatically on every push to `master`
- make the package consumable from other GitHub repositories through GitHub Packages
- keep versioning automatic and simple
- validate the repository before publishing

## Non-Goals

- semantic version inference from commit messages
- tag-based release publishing
- publishing the whole solution as NuGet packages
- publishing prerelease packages from feature branches

## Workflow Behavior

Trigger:

- `push` to `master`

Steps:

1. checkout code
2. install .NET SDK
3. restore dependencies
4. run tests
5. compute package version from `VersionPrefix` + `${{ github.run_number }}`
6. pack `src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj`
7. publish the generated package to GitHub Packages
8. optionally upload the `.nupkg` as a workflow artifact for inspection

## Project Changes

`WiSave.Portal.Contracts.csproj` should include package metadata:

- `PackageId`
- `VersionPrefix`
- `Authors`
- `Description`
- `RepositoryUrl`
- `PackageReadmeFile`
- `PackageTags`
- `PackageLicenseExpression` if desired

A `README.md` should be added under the contracts project and included in the package so the GitHub Packages page is usable.

## Versioning

Use fixed `VersionPrefix` in the project file, for example:

```xml
<VersionPrefix>0.1</VersionPrefix>
```

The workflow computes:

```text
0.1.${GITHUB_RUN_NUMBER}
```

Examples:

- run `15` => `0.1.15`
- run `16` => `0.1.16`

This is intentionally simple and stable for internal package consumption.

## Publishing Target

Publish to GitHub Packages using the repository owner namespace. Authentication will use the built-in `GITHUB_TOKEN`.

The workflow should grant:

- `contents: read`
- `packages: write`

## Consumer Expectations

Other repositories that want to consume `WiSave.Portal.Contracts` will need:

- GitHub Packages configured as a NuGet source
- credentials that can read packages from the account or organization feed
- a package reference to the published version

## Risks

- every merge to `master` creates a stable package, so package history will grow quickly
- `github.run_number` is repository-wide, not package-specific
- if package metadata is incomplete, the resulting package page will be poor even if publishing succeeds

## Recommendation

Implement a dedicated workflow for `WiSave.Portal.Contracts` publishing on `master`, with package metadata added to the project and version generation based on `VersionPrefix + run number`.
