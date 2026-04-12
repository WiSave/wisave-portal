# WiSave.Portal.Contracts GitHub Actions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a GitHub Actions workflow that publishes `WiSave.Portal.Contracts` to GitHub Packages on every push to `master`.

**Architecture:** Keep package versioning simple by storing `VersionPrefix` in `WiSave.Portal.Contracts.csproj` and letting the workflow append `${{ github.run_number }}` at pack time. Publish only the contracts package, but validate the repo by running the portal test project first.

**Tech Stack:** GitHub Actions, .NET 10, NuGet, GitHub Packages

---

### Task 1: Add Package Metadata

**Files:**
- Modify: `src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj`
- Create: `src/WiSave.Portal.Contracts/README.md`

- [ ] **Step 1: Add package metadata to the contracts project**

Set `PackageId`, `VersionPrefix`, `Authors`, `Description`, `RepositoryUrl`, `PackageTags`, and `PackageReadmeFile` in `WiSave.Portal.Contracts.csproj`.

- [ ] **Step 2: Add a package README**

Create `src/WiSave.Portal.Contracts/README.md` with package purpose, versioning summary, and a minimal consumer example.

- [ ] **Step 3: Run a local pack command**

Run: `dotnet pack src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj -c Release -p:Version=0.1.1 -o /tmp/portal-contracts-pack`
Expected: PASS and produce a `.nupkg` that includes the README metadata.

### Task 2: Add GitHub Packages Workflow

**Files:**
- Create: `.github/workflows/publish-portal-contracts.yml`

- [ ] **Step 1: Add the workflow**

Create a workflow that:
- triggers on `push` to `master`
- uses current actions versions
- sets `contents: read` and `packages: write`
- restores, builds, tests, packs, and publishes only `WiSave.Portal.Contracts`
- computes package version from project `VersionPrefix` + `github.run_number`
- uploads the package as an artifact in addition to publishing

- [ ] **Step 2: Inspect the workflow for consistency**

Verify the project path, package source URL, and version computation match the package metadata.

### Task 3: Verify Repository State

**Files:**
- Modify: files from Tasks 1-2

- [ ] **Step 1: Run the portal test project**

Run: `dotnet test tests/WiSave.Portal.Tests/WiSave.Portal.Tests.csproj`
Expected: PASS

- [ ] **Step 2: Re-run local packing using the workflow version shape**

Run: `dotnet pack src/WiSave.Portal.Contracts/WiSave.Portal.Contracts.csproj -c Release -p:Version=0.1.999 -o /tmp/portal-contracts-pack`
Expected: PASS

- [ ] **Step 3: Summarize follow-up**

Report:
- which secret or token assumptions the workflow uses
- how the downstream repo should configure GitHub Packages
