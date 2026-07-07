# Beacon Stream Milestone 2 Display Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Windows display lifecycle checkpoint for the full Beacon Stream objective: per-client virtual display leases, exact mode verification, virtual-primary session topology, physical-primary restore, manual recovery, truthful HDR reporting, and phone-free validation.

**Architecture:** Keep display policy in `Beacon.Core`, isolate Windows and SudoVDA calls in a new `Beacon.Platform.Windows` project, and expose a manual `Beacon.DisplayProbe` console for real-machine checks that are separated from fast tests. Real monitor changes only happen through the probe or explicit server recovery actions; fast tests use fakes.

**Tech Stack:** .NET 10, C# 14, xUnit, Windows DisplayConfig/PInvoke through an adapter boundary, SudoVDA device IO through an adapter boundary, ASP.NET Core minimal API, GPL-3.0.

---

## Scope Guard

This plan implements Milestone 2 from `docs/superpowers/specs/2026-06-03-personal-streaming-orchestrator-design.md` as one checkpoint inside the broader project objective.

In scope:

- Per-client display identity from `DisplayLease.CreateDisplayId(ClientId)`.
- Create or verify a leased virtual display before app launch.
- Apply exact client mode, including `2560x1600@120` when the profile asks for it and the backend can provide it.
- Make the leased virtual display primary for a session while preventing mirror mode by default.
- Keep disconnect separate from virtual display removal.
- Remove a display only when the client is inactive **AND** no owned launched process, child process, or tracked window remains.
- Restore and verify physical primary after cleanup or recovery.
- Keep a leased virtual display present while owned work still exists, without leaving the laptop panel inactive.
- Manual recovery override for WPF/server-admin and local test clients.
- Truthful HDR capability reporting for `off`, `prefer`, and `require`.
- Manual Windows/SudoVDA integration checks that do not need the phone.

Out of scope:

- Real streaming transport and encoder work.
- Real Android APK changes.
- Game collection normalization.
- Per-game render-setting control.
- Promising HDR success when Windows or SudoVDA reports SDR only.
- Copying upstream C++ source before an extraction-map row names the exact source and destination.

## Source Boundary Notes

Apollo reference files audited for interface shape only:

- `Apollo-source/src/platform/windows/virtual_display.h`
- `Apollo-source/src/platform/windows/virtual_display.cpp`
- `Apollo-source/third-party/sudovda/sudovda.h`
- `Apollo-source/tools/sudovda_hdr_probe.cpp`

The Beacon implementation in this plan is a C# adapter boundary and original policy code. If later work copies or adapts upstream code, update `docs/extraction-map.md` before the copied code lands.

## File Structure

Create:

- `src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj` contains Windows-specific display backend code.
- `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs` implements `IDisplayBackend` using the adapter interface.
- `src/Beacon.Platform.Windows/Displays/IWindowsDisplayApi.cs` defines the Windows/SudoVDA adapter seam.
- `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs` contains real PInvoke and SudoVDA device operations.
- `src/Beacon.Platform.Windows/Displays/DisplayTopologySnapshot.cs` represents before/after topology facts.
- `src/Beacon.Platform.Windows/Displays/DisplayOperationLogEntry.cs` captures topology decisions.
- `src/Beacon.Platform.Windows/Displays/WindowsDisplayDiagnostics.cs` formats driver, topology, and HDR diagnostics.
- `src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj` provides manually runnable real Windows display commands.
- `src/Beacon.DisplayProbe/DisplayProbeCommandLine.cs` parses probe commands.
- `src/Beacon.DisplayProbe/Program.cs` wires the probe.
- `tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj` contains fast adapter-backed tests.
- `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs` verifies policy over a fake Windows adapter.
- `tests/Beacon.Platform.Windows.Tests/Displays/FakeWindowsDisplayApi.cs` is the deterministic adapter fake.
- `tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj` tests probe command parsing without changing displays.
- `tests/Beacon.DisplayProbe.Tests/DisplayProbeCommandLineTests.cs` verifies probe argument handling.
- `docs/windows-display-backend.md` documents manual display checks and safety boundaries.

Modify:

- `Beacon.slnx` adds the two new source projects and two new test projects.
- `src/Beacon.Core/Displays/IDisplayBackend.cs` returns richer display operation results without introducing Windows dependencies.
- `src/Beacon.Core/Displays/DisplayLeaseManager.cs` calls create/verify, HDR negotiation, primary activation, physical restore, cleanup, and recovery methods.
- `src/Beacon.Core/Displays/FakeDisplayBackend.cs` implements the richer test contract.
- `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs` adds lease cleanup, restore, and recovery policy coverage.
- `src/Beacon.Server/Api/ClientEndpoints.cs` adds manual display recovery endpoints.
- `tests/Beacon.Server.Tests/ClientApiTests.cs` verifies recovery API results.
- `.github/workflows/ci.yml` includes the new fast projects, excluding manual real-display probe commands.
- `docs/extraction-map.md` gets a reference-only audit note if any upstream file becomes more than research.

## Sync Contract

Every implementation stage follows this loop:

```text
Implement
Validate static and dynamic checks
Sync
Refactor
Validate static and dynamic checks
Sync
```

Sync means:

```powershell
git status --short
git add <coherent files>
git commit -m "<checkpoint message>"
git push -u origin codex/milestone-2-display-backend
```

No substantial work remains local-only across a context compaction.

## Validation Commands

Static validation:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Dynamic validation:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Manual Windows display validation:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
dotnet run --project src/Beacon.DisplayProbe -- ensure --client z-fold-7 --width 2560 --height 1600 --refresh 120 --hdr prefer
dotnet run --project src/Beacon.DisplayProbe -- primary --client z-fold-7
dotnet run --project src/Beacon.DisplayProbe -- restore-physical
dotnet run --project src/Beacon.DisplayProbe -- remove --client z-fold-7
```

The manual commands are never run by CI.

---

### Task 1: Add Windows Platform And Probe Projects

**Files:**
- Create: `src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj`
- Create: `src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj`
- Create: `tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj`
- Create: `tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj`
- Modify: `Beacon.slnx`

- [ ] **Step 1: Create projects**

Run:

```powershell
dotnet new classlib --framework net10.0-windows --name Beacon.Platform.Windows --output src/Beacon.Platform.Windows
dotnet new console --framework net10.0-windows --name Beacon.DisplayProbe --output src/Beacon.DisplayProbe
dotnet new xunit --framework net10.0-windows --name Beacon.Platform.Windows.Tests --output tests/Beacon.Platform.Windows.Tests
dotnet new xunit --framework net10.0-windows --name Beacon.DisplayProbe.Tests --output tests/Beacon.DisplayProbe.Tests
dotnet sln Beacon.slnx add src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj
dotnet sln Beacon.slnx add src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj
dotnet sln Beacon.slnx add tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj
dotnet sln Beacon.slnx add tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj
dotnet add src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj reference src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj
dotnet add tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj reference src/Beacon.Core/Beacon.Core.csproj
dotnet add tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj reference src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj
dotnet add tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj reference src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj
```

Expected: every command exits 0.

- [ ] **Step 2: Remove generated placeholder files**

Run:

```powershell
Get-ChildItem -Recurse src/Beacon.Platform.Windows,src/Beacon.DisplayProbe,tests/Beacon.Platform.Windows.Tests,tests/Beacon.DisplayProbe.Tests -Include Class1.cs,UnitTest1.cs
```

Expected before deletion: generated placeholder files are listed.

Delete only those generated files with `Remove-Item -LiteralPath <listed file>`.

Run again:

```powershell
Get-ChildItem -Recurse src/Beacon.Platform.Windows,src/Beacon.DisplayProbe,tests/Beacon.Platform.Windows.Tests,tests/Beacon.DisplayProbe.Tests -Include Class1.cs,UnitTest1.cs
```

Expected after deletion: no files are listed.

- [ ] **Step 3: Verify scaffold builds**

Run:

```powershell
dotnet build Beacon.slnx -warnaserror
```

Expected: build succeeds with 0 warnings and 0 errors.

- [ ] **Step 4: Sync scaffold checkpoint**

Run:

```powershell
git status --short
git add Beacon.slnx src/Beacon.Platform.Windows src/Beacon.DisplayProbe tests/Beacon.Platform.Windows.Tests tests/Beacon.DisplayProbe.Tests
git commit -m "Add Windows display backend projects"
git push -u origin codex/milestone-2-display-backend
```

Expected: push succeeds.

### Task 2: Expand Core Display Contract With TDD

**Files:**
- Modify: `src/Beacon.Core/Displays/IDisplayBackend.cs`
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`

- [ ] **Step 1: Write failing lease tests**

Add these tests to `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`:

```csharp
[Fact]
public async Task EnsureLeaseAsync_WhenBackendCannotProvideVirtualDisplay_ReturnsDiagnosticAndNeverUsesPhysicalFallback()
{
    var backend = new FakeDisplayBackend();
    backend.NextEnsureResult = DisplayEnsureResult.Fail("SudoVDA driver unavailable");
    var manager = new DisplayLeaseManager(backend);
    ClientProfile profile = ClientProfile.DefaultFor(new ClientId("z-fold-7"));

    DisplayLeaseResult result = await manager.EnsureLeaseAsync(profile, CancellationToken.None);

    Assert.False(result.Success);
    Assert.Null(result.Lease);
    Assert.Contains("SudoVDA driver unavailable", result.Error);
    Assert.Contains("refusing to fall back to physical display", result.Error);
    Assert.Empty(backend.RestorePhysicalPrimaryCalls);
}

[Fact]
public async Task CleanupIfAllowedAsync_WhenOwnedWindowRemains_RestoresPhysicalPrimaryButKeepsVirtualDisplay()
{
    var backend = new FakeDisplayBackend();
    var manager = new DisplayLeaseManager(backend);

    bool removed = await manager.CleanupIfAllowedAsync(
        "client-z-fold-7",
        clientActive: false,
        ownedProcessRunning: false,
        ownedWindowRemaining: true,
        CancellationToken.None);

    Assert.False(removed);
    Assert.Empty(backend.RemovedDisplays);
    Assert.Single(backend.RestorePhysicalPrimaryCalls);
}

[Fact]
public async Task CleanupIfAllowedAsync_WhenClientInactiveAndNoOwnedWork_RemovesDisplayAfterPhysicalPrimaryRestore()
{
    var backend = new FakeDisplayBackend();
    var manager = new DisplayLeaseManager(backend);

    bool removed = await manager.CleanupIfAllowedAsync(
        "client-z-fold-7",
        clientActive: false,
        ownedProcessRunning: false,
        ownedWindowRemaining: false,
        CancellationToken.None);

    Assert.True(removed);
    Assert.Single(backend.RestorePhysicalPrimaryCalls);
    Assert.Equal("client-z-fold-7", Assert.Single(backend.RemovedDisplays));
}
```

- [ ] **Step 2: Verify tests fail for the expected reason**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: the two new cleanup tests fail because `CleanupIfAllowedAsync` does not call physical restore yet.

- [ ] **Step 3: Implement minimal contract expansion**

Change `IDisplayBackend` to this shape:

```csharp
namespace Beacon.Core.Displays;

public interface IDisplayBackend
{
    Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(string displayId, int width, int height, int refreshHz, HdrPreference hdrPreference, CancellationToken cancellationToken);
    Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(string reason, CancellationToken cancellationToken);
    Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayEnsureResult(bool Success, string? Error, bool HdrEnabled = false, string? HdrReason = null)
{
    public static DisplayEnsureResult Ok(bool hdrEnabled = false, string? hdrReason = null) => new(true, null, hdrEnabled, hdrReason);
    public static DisplayEnsureResult Fail(string error) => new(false, error);
}

public sealed record DisplayRestoreResult(bool Success, string? Error)
{
    public static DisplayRestoreResult Ok() => new(true, null);
    public static DisplayRestoreResult Fail(string error) => new(false, error);
}

public sealed record DisplayRemoveResult(bool Success, string? Error)
{
    public static DisplayRemoveResult Ok() => new(true, null);
    public static DisplayRemoveResult Fail(string error) => new(false, error);
}
```

Update `DisplayLeaseManager.EnsureLeaseAsync` to pass `profile.Display.HdrPreference`.

Update `CleanupIfAllowedAsync`:

```csharp
public async Task<bool> CleanupIfAllowedAsync(
    string displayId,
    bool clientActive,
    bool ownedProcessRunning,
    bool ownedWindowRemaining,
    CancellationToken cancellationToken)
{
    if (clientActive)
    {
        return false;
    }

    if (ownedProcessRunning || ownedWindowRemaining)
    {
        await displayBackend.RestorePhysicalPrimaryAsync("client inactive but owned work remains", cancellationToken);
        return false;
    }

    await displayBackend.RestorePhysicalPrimaryAsync("client inactive and no owned work remains", cancellationToken);
    await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
    return true;
}
```

Update `FakeDisplayBackend` with `RestorePhysicalPrimaryCalls` and `RemovedDisplays` collections.

- [ ] **Step 4: Verify core display tests pass**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: all `DisplayLeaseManagerTests` pass.

### Task 3: Add Windows Adapter Contract With TDD

**Files:**
- Create: `src/Beacon.Platform.Windows/Displays/IWindowsDisplayApi.cs`
- Create: `src/Beacon.Platform.Windows/Displays/DisplayTopologySnapshot.cs`
- Create: `src/Beacon.Platform.Windows/Displays/DisplayOperationLogEntry.cs`
- Create: `src/Beacon.Platform.Windows/Displays/WindowsDisplayDiagnostics.cs`
- Create: `tests/Beacon.Platform.Windows.Tests/Displays/FakeWindowsDisplayApi.cs`
- Create: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`

- [ ] **Step 1: Write failing adapter-backed tests**

Create `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`:

```csharp
using Beacon.Core.Displays;
using Beacon.Platform.Windows.Displays;

namespace Beacon.Platform.Windows.Tests.Displays;

public sealed class WindowsDisplayBackendTests
{
    [Fact]
    public async Task EnsureVirtualDisplayAsync_WhenDriverIsUnavailable_ReturnsDiagnosticAndDoesNotChangeTopology()
    {
        var api = new FakeWindowsDisplayApi { DriverReady = false, DriverDiagnostic = "SudoVDA driver is not installed" };
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120, HdrPreference.Prefer, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("SudoVDA driver is not installed", result.Error);
        Assert.Empty(api.CreatedDisplays);
        Assert.Empty(api.PrimaryRequests);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_CreatesDisplayAndRejectsMirrorTopology()
    {
        var api = FakeWindowsDisplayApi.Ready();
        api.AfterCreateTopology = DisplayTopologySnapshot.Mirrored(
            physicalDisplayName: @"\\.\DISPLAY1",
            virtualDisplayName: @"\\.\DISPLAY7",
            width: 2560,
            height: 1600,
            refreshHz: 120);
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120, HdrPreference.Prefer, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("mirror mode", result.Error);
        Assert.Contains("client-z-fold-7", api.CreatedDisplays);
    }

    [Fact]
    public async Task EnsureVirtualDisplayAsync_VerifiesExactModeAndMakesVirtualPrimary()
    {
        var api = FakeWindowsDisplayApi.Ready();
        api.AfterCreateTopology = DisplayTopologySnapshot.Extended(
            physicalDisplayName: @"\\.\DISPLAY1",
            virtualDisplayName: @"\\.\DISPLAY7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: false);
        api.AfterPrimaryTopology = DisplayTopologySnapshot.Extended(
            physicalDisplayName: @"\\.\DISPLAY1",
            virtualDisplayName: @"\\.\DISPLAY7",
            width: 2560,
            height: 1600,
            refreshHz: 120,
            virtualPrimary: true);
        var backend = new WindowsDisplayBackend(api);

        DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120, HdrPreference.Prefer, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(("client-z-fold-7", 2560, 1600, 120), Assert.Single(api.CreatedDisplays));
        Assert.Equal("client-z-fold-7", Assert.Single(api.PrimaryRequests));
    }
}
```

- [ ] **Step 2: Verify adapter tests fail for missing types**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj
```

Expected: compile fails because `WindowsDisplayBackend`, `IWindowsDisplayApi`, `FakeWindowsDisplayApi`, and `DisplayTopologySnapshot` do not exist.

- [ ] **Step 3: Implement adapter records and fake**

Create `src/Beacon.Platform.Windows/Displays/IWindowsDisplayApi.cs`:

```csharp
using Beacon.Core.Displays;

namespace Beacon.Platform.Windows.Displays;

public interface IWindowsDisplayApi
{
    DisplayDriverStatus GetDriverStatus();
    Task<DisplayTopologySnapshot> QueryTopologyAsync(CancellationToken cancellationToken);
    Task<DisplayApiResult> CreateVirtualDisplayAsync(string displayId, int width, int height, int refreshHz, CancellationToken cancellationToken);
    Task<DisplayApiResult> SetVirtualPrimaryAsync(string displayId, CancellationToken cancellationToken);
    Task<DisplayApiResult> RestorePhysicalPrimaryAsync(CancellationToken cancellationToken);
    Task<DisplayApiResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken);
    Task<DisplayHdrCapability> QueryHdrCapabilityAsync(string displayId, CancellationToken cancellationToken);
}

public sealed record DisplayDriverStatus(bool Ready, string Diagnostic);
public sealed record DisplayApiResult(bool Success, string? Error)
{
    public static DisplayApiResult Ok() => new(true, null);
    public static DisplayApiResult Fail(string error) => new(false, error);
}
public sealed record DisplayHdrCapability(bool Supported, bool Enabled, string Reason);
```

Create `src/Beacon.Platform.Windows/Displays/DisplayTopologySnapshot.cs` with records for active paths, primary flag, mirror detection, exact mode matching, and physical-primary verification.

Create `tests/Beacon.Platform.Windows.Tests/Displays/FakeWindowsDisplayApi.cs` implementing `IWindowsDisplayApi` with call lists for `CreatedDisplays`, `PrimaryRequests`, `RestoreRequests`, and `RemovedDisplays`.

- [ ] **Step 4: Implement minimal backend**

Create `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`:

```csharp
using Beacon.Core.Displays;

namespace Beacon.Platform.Windows.Displays;

public sealed class WindowsDisplayBackend(IWindowsDisplayApi api) : IDisplayBackend
{
    public async Task<DisplayEnsureResult> EnsureVirtualDisplayAsync(string displayId, int width, int height, int refreshHz, HdrPreference hdrPreference, CancellationToken cancellationToken)
    {
        DisplayDriverStatus status = api.GetDriverStatus();
        if (!status.Ready)
        {
            return DisplayEnsureResult.Fail(status.Diagnostic);
        }

        DisplayApiResult createResult = await api.CreateVirtualDisplayAsync(displayId, width, height, refreshHz, cancellationToken);
        if (!createResult.Success)
        {
            return DisplayEnsureResult.Fail(createResult.Error ?? "Virtual display creation failed.");
        }

        DisplayTopologySnapshot afterCreate = await api.QueryTopologyAsync(cancellationToken);
        if (afterCreate.IsMirrorMode)
        {
            return DisplayEnsureResult.Fail($"Refusing mirror mode for {displayId}.");
        }

        if (!afterCreate.HasDisplayMode(displayId, width, height, refreshHz))
        {
            return DisplayEnsureResult.Fail($"Virtual display {displayId} did not expose {width}x{height}@{refreshHz}.");
        }

        DisplayApiResult primaryResult = await api.SetVirtualPrimaryAsync(displayId, cancellationToken);
        if (!primaryResult.Success)
        {
            return DisplayEnsureResult.Fail(primaryResult.Error ?? $"Unable to make {displayId} primary.");
        }

        DisplayTopologySnapshot afterPrimary = await api.QueryTopologyAsync(cancellationToken);
        if (!afterPrimary.IsPrimary(displayId))
        {
            return DisplayEnsureResult.Fail($"Virtual display {displayId} was not primary after topology apply.");
        }

        DisplayHdrCapability hdr = await api.QueryHdrCapabilityAsync(displayId, cancellationToken);
        return NegotiateHdr(hdrPreference, hdr);
    }

    public async Task<DisplayRestoreResult> RestorePhysicalPrimaryAsync(string reason, CancellationToken cancellationToken)
    {
        DisplayApiResult restore = await api.RestorePhysicalPrimaryAsync(cancellationToken);
        if (!restore.Success)
        {
            return DisplayRestoreResult.Fail(restore.Error ?? $"Physical primary restore failed for {reason}.");
        }

        DisplayTopologySnapshot topology = await api.QueryTopologyAsync(cancellationToken);
        return topology.PhysicalPrimaryVerified
            ? DisplayRestoreResult.Ok()
            : DisplayRestoreResult.Fail($"Physical primary restore was not verified for {reason}.");
    }

    public async Task<DisplayRemoveResult> RemoveVirtualDisplayAsync(string displayId, CancellationToken cancellationToken)
    {
        DisplayApiResult remove = await api.RemoveVirtualDisplayAsync(displayId, cancellationToken);
        return remove.Success ? DisplayRemoveResult.Ok() : DisplayRemoveResult.Fail(remove.Error ?? $"Unable to remove {displayId}.");
    }

    private static DisplayEnsureResult NegotiateHdr(HdrPreference preference, DisplayHdrCapability capability)
    {
        if (preference == HdrPreference.Off)
        {
            return DisplayEnsureResult.Ok(hdrEnabled: false, hdrReason: "HDR disabled by profile.");
        }

        if (capability.Supported && capability.Enabled)
        {
            return DisplayEnsureResult.Ok(hdrEnabled: true, hdrReason: capability.Reason);
        }

        return preference == HdrPreference.Require
            ? DisplayEnsureResult.Fail($"HDR required but unavailable: {capability.Reason}")
            : DisplayEnsureResult.Ok(hdrEnabled: false, hdrReason: capability.Reason);
    }
}
```

- [ ] **Step 5: Verify adapter tests pass**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj
```

Expected: all tests pass.

### Task 4: Add HDR Truth Tests

**Files:**
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`

- [ ] **Step 1: Write failing HDR tests**

Add:

```csharp
[Fact]
public async Task EnsureVirtualDisplayAsync_WhenHdrPreferredAndDriverReportsSdr_ReturnsSdrWithReason()
{
    var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
    api.HdrCapability = new DisplayHdrCapability(Supported: false, Enabled: false, Reason: "Windows Advanced Color reports SDR only");
    var backend = new WindowsDisplayBackend(api);

    DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120, HdrPreference.Prefer, CancellationToken.None);

    Assert.True(result.Success, result.Error);
    Assert.False(result.HdrEnabled);
    Assert.Equal("Windows Advanced Color reports SDR only", result.HdrReason);
}

[Fact]
public async Task EnsureVirtualDisplayAsync_WhenHdrRequiredAndDriverReportsSdr_FailsBeforeLaunch()
{
    var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
    api.HdrCapability = new DisplayHdrCapability(Supported: false, Enabled: false, Reason: "virtual display exposes no HDR metadata");
    var backend = new WindowsDisplayBackend(api);

    DisplayEnsureResult result = await backend.EnsureVirtualDisplayAsync("client-z-fold-7", 2560, 1600, 120, HdrPreference.Require, CancellationToken.None);

    Assert.False(result.Success);
    Assert.Contains("HDR required", result.Error);
    Assert.Contains("virtual display exposes no HDR metadata", result.Error);
}
```

- [ ] **Step 2: Verify HDR tests fail before implementation**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter Hdr
```

Expected: tests fail until `NegotiateHdr` and fake HDR capability support are complete.

- [ ] **Step 3: Implement HDR fake data and negotiation**

Add `HdrCapability` to `FakeWindowsDisplayApi` and complete `WindowsDisplayBackend.NegotiateHdr` exactly as in Task 3.

- [ ] **Step 4: Verify HDR tests pass**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter Hdr
```

Expected: both HDR tests pass.

### Task 5: Add Restore Reconciliation And Manual Recovery Policy

**Files:**
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
- Modify: `src/Beacon.Core/Displays/FakeDisplayBackend.cs`
- Modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayBackend.cs`
- Modify: `tests/Beacon.Platform.Windows.Tests/Displays/WindowsDisplayBackendTests.cs`

- [ ] **Step 1: Write failing manual recovery test**

Add to `DisplayLeaseManagerTests`:

```csharp
[Fact]
public async Task RecoverDisplayAsync_ManualOverrideRestoresPhysicalPrimaryAndRemovesVirtualDisplay()
{
    var backend = new FakeDisplayBackend();
    var manager = new DisplayLeaseManager(backend);

    DisplayRecoveryResult result = await manager.RecoverDisplayAsync("client-z-fold-7", CancellationToken.None);

    Assert.True(result.Success, result.Error);
    Assert.Single(backend.RestorePhysicalPrimaryCalls);
    Assert.Equal("client-z-fold-7", Assert.Single(backend.RemovedDisplays));
}
```

- [ ] **Step 2: Verify recovery test fails for missing API**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter RecoverDisplayAsync
```

Expected: compile fails because `RecoverDisplayAsync` and `DisplayRecoveryResult` do not exist.

- [ ] **Step 3: Implement recovery result and manager method**

Add to `IDisplayBackend.cs`:

```csharp
public sealed record DisplayRecoveryResult(bool Success, string? Error)
{
    public static DisplayRecoveryResult Ok() => new(true, null);
    public static DisplayRecoveryResult Fail(string error) => new(false, error);
}
```

Add to `DisplayLeaseManager`:

```csharp
public async Task<DisplayRecoveryResult> RecoverDisplayAsync(string displayId, CancellationToken cancellationToken)
{
    DisplayRestoreResult restore = await displayBackend.RestorePhysicalPrimaryAsync("manual recovery", cancellationToken);
    if (!restore.Success)
    {
        return DisplayRecoveryResult.Fail(restore.Error ?? "Physical primary restore failed during manual recovery.");
    }

    DisplayRemoveResult remove = await displayBackend.RemoveVirtualDisplayAsync(displayId, cancellationToken);
    if (!remove.Success)
    {
        return DisplayRecoveryResult.Fail(remove.Error ?? $"Unable to remove {displayId} during manual recovery.");
    }

    return DisplayRecoveryResult.Ok();
}
```

- [ ] **Step 4: Write failing restore reconciliation test**

Add to `WindowsDisplayBackendTests`:

```csharp
[Fact]
public async Task RestorePhysicalPrimaryAsync_WhenFirstTopologyIsStale_ReconcilesUntilVerified()
{
    var api = FakeWindowsDisplayApi.ReadyWithGoodTopology();
    api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.Extended(
        physicalDisplayName: @"\\.\DISPLAY1",
        virtualDisplayName: @"\\.\DISPLAY7",
        width: 2560,
        height: 1600,
        refreshHz: 120,
        virtualPrimary: true));
    api.RestoreTopologies.Enqueue(DisplayTopologySnapshot.PhysicalOnly(@"\\.\DISPLAY1", 2560, 1600, 120));
    var backend = new WindowsDisplayBackend(api);

    DisplayRestoreResult result = await backend.RestorePhysicalPrimaryAsync("test reconciliation", CancellationToken.None);

    Assert.True(result.Success, result.Error);
    Assert.Equal(2, api.RestoreRequests.Count);
}
```

- [ ] **Step 5: Implement reconciliation without elapsed-time cancellation**

Change `WindowsDisplayBackend.RestorePhysicalPrimaryAsync` to ask the adapter for restore, query topology, and repeat only while the adapter reports a stale topology that can still be reconciled. The fake exposes a finite queue; the real adapter bases the decision on actual topology state. Do not add elapsed-time cancellation.

- [ ] **Step 6: Verify core and platform tests pass**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj
```

Expected: both commands pass.

### Task 6: Add Server Manual Recovery Endpoints

**Files:**
- Modify: `src/Beacon.Server/Api/ClientEndpoints.cs`
- Modify: `tests/Beacon.Server.Tests/ClientApiTests.cs`

- [ ] **Step 1: Write failing API test**

Add:

```csharp
[Fact]
public async Task PostDisplayRecovery_ReturnsDiagnosticResultForClientDisplay()
{
    using WebApplicationFactory<Program> factory = new();
    using HttpClient client = factory.CreateClient();

    HttpResponseMessage response = await client.PostAsync("/clients/z-fold-7/display/recover", content: null);

    Assert.True(response.IsSuccessStatusCode);
    string body = await response.Content.ReadAsStringAsync();
    Assert.Contains("client-z-fold-7", body);
}
```

- [ ] **Step 2: Verify API test fails**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter PostDisplayRecovery
```

Expected: test fails with 404 because the endpoint does not exist.

- [ ] **Step 3: Implement endpoint**

Add a POST endpoint:

```csharp
group.MapPost("/{clientId}/display/recover", async (
    string clientId,
    DisplayLeaseManager displayLeaseManager,
    CancellationToken cancellationToken) =>
{
    string displayId = DisplayLease.CreateDisplayId(new ClientId(clientId));
    DisplayRecoveryResult result = await displayLeaseManager.RecoverDisplayAsync(displayId, cancellationToken);
    return result.Success
        ? Results.Ok(new { displayId, recovered = true })
        : Results.Problem(result.Error, statusCode: StatusCodes.Status503ServiceUnavailable);
});
```

- [ ] **Step 4: Verify API test passes**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter PostDisplayRecovery
```

Expected: test passes.

### Task 7: Add Manual Display Probe

**Files:**
- Create: `src/Beacon.DisplayProbe/DisplayProbeCommandLine.cs`
- Modify: `src/Beacon.DisplayProbe/Program.cs`
- Create: `tests/Beacon.DisplayProbe.Tests/DisplayProbeCommandLineTests.cs`
- Create: `docs/windows-display-backend.md`

- [ ] **Step 1: Write failing command parser tests**

Create `tests/Beacon.DisplayProbe.Tests/DisplayProbeCommandLineTests.cs`:

```csharp
using Beacon.DisplayProbe;

namespace Beacon.DisplayProbe.Tests;

public sealed class DisplayProbeCommandLineTests
{
    [Fact]
    public void ParseEnsureCommandPreservesSixteenByTenAndRefresh()
    {
        DisplayProbeCommand command = DisplayProbeCommandLine.Parse([
            "ensure",
            "--client", "z-fold-7",
            "--width", "2560",
            "--height", "1600",
            "--refresh", "120",
            "--hdr", "prefer"
        ]);

        var ensure = Assert.IsType<EnsureDisplayProbeCommand>(command);
        Assert.Equal("z-fold-7", ensure.ClientId);
        Assert.Equal(2560, ensure.Width);
        Assert.Equal(1600, ensure.Height);
        Assert.Equal(120, ensure.RefreshHz);
        Assert.Equal("prefer", ensure.Hdr);
    }

    [Fact]
    public void ParseRestorePhysicalCommandHasNoClientRequirement()
    {
        DisplayProbeCommand command = DisplayProbeCommandLine.Parse(["restore-physical"]);

        Assert.IsType<RestorePhysicalDisplayProbeCommand>(command);
    }
}
```

- [ ] **Step 2: Verify parser tests fail**

Run:

```powershell
dotnet test tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj
```

Expected: compile fails because parser types do not exist.

- [ ] **Step 3: Implement parser**

Create records:

```csharp
namespace Beacon.DisplayProbe;

public abstract record DisplayProbeCommand;
public sealed record StatusDisplayProbeCommand : DisplayProbeCommand;
public sealed record EnsureDisplayProbeCommand(string ClientId, int Width, int Height, int RefreshHz, string Hdr) : DisplayProbeCommand;
public sealed record PrimaryDisplayProbeCommand(string ClientId) : DisplayProbeCommand;
public sealed record RestorePhysicalDisplayProbeCommand : DisplayProbeCommand;
public sealed record RemoveDisplayProbeCommand(string ClientId) : DisplayProbeCommand;
```

Implement `DisplayProbeCommandLine.Parse(string[] args)` with explicit command names: `status`, `ensure`, `primary`, `restore-physical`, and `remove`.

- [ ] **Step 4: Implement probe program**

`Program.cs` maps parsed commands to `WindowsDisplayBackend` operations and prints JSON-like diagnostics with display id, requested mode, HDR result, and success/error. `status` only queries and prints topology; it does not alter displays.

- [ ] **Step 5: Add manual probe docs**

Create `docs/windows-display-backend.md` with:

- Prerequisites: SudoVDA installed and enabled, Visual Studio/WDK only needed for driver rebuild work.
- Fast tests do not touch display topology.
- `status` is read-only.
- `ensure`, `primary`, `restore-physical`, and `remove` can change Windows display topology.
- Recovery order: run `restore-physical`, then `remove --client <id>` if the laptop panel is not primary.
- HDR result is truthful: SDR fallback in `prefer`, pre-launch failure in `require`.

- [ ] **Step 6: Verify probe parser tests pass**

Run:

```powershell
dotnet test tests/Beacon.DisplayProbe.Tests/Beacon.DisplayProbe.Tests.csproj
```

Expected: tests pass.

### Task 8: Implement Real Windows Adapter

**Files:**
- Create: `src/Beacon.Platform.Windows/Displays/WindowsDisplayApi.cs`
- Modify: `src/Beacon.Platform.Windows/Displays/WindowsDisplayDiagnostics.cs`
- Modify: `docs/extraction-map.md` only if code is copied or adapted from upstream.

- [ ] **Step 1: Write adapter shape tests only where deterministic**

Do not unit-test raw PInvoke by mocking Windows. Keep raw Windows calls in `WindowsDisplayApi` and test `WindowsDisplayBackend` against `FakeWindowsDisplayApi`.

- [ ] **Step 2: Implement read-only topology query**

Implement `QueryTopologyAsync` with `QueryDisplayConfig` and `DisplayConfigGetDeviceInfo`. Map:

- active paths,
- source name,
- target friendly name,
- source position,
- active resolution,
- refresh,
- primary flag,
- virtual display match,
- mirror detection.

- [ ] **Step 3: Implement SudoVDA driver status**

Implement:

- driver open status,
- protocol compatibility result when available,
- diagnostic string equivalent to installed/enabled/protocol mismatch/stale handle.

- [ ] **Step 4: Implement create/remove**

Use the SudoVDA device IO boundary to create and remove a display by deterministic per-client id. The first working version may support one deterministic GUID per client id and must log the mapping.

- [ ] **Step 5: Implement exact mode and primary topology apply**

Use Windows display topology APIs to:

- apply requested width, height, and refresh,
- set the virtual display primary for the session,
- keep the physical display extended unless blackout policy is later enabled,
- reject mirror mode after apply.

- [ ] **Step 6: Implement physical-primary restore**

Restore the physical display as primary and verify topology after each apply. If the first query reports stale topology, reapply based on the fresh topology state. Do not use elapsed-time cancellation.

- [ ] **Step 7: Implement HDR capability query**

Use Windows Advanced Color queries to report:

- HDR/WCG support,
- HDR enabled state,
- bits per channel,
- reason when SDR only.

Do not force HDR enabled when Windows reports no HDR support.

- [ ] **Step 8: Build real adapter**

Run:

```powershell
dotnet build src/Beacon.Platform.Windows/Beacon.Platform.Windows.csproj -warnaserror
dotnet build src/Beacon.DisplayProbe/Beacon.DisplayProbe.csproj -warnaserror
```

Expected: both builds pass.

### Task 9: Static And Dynamic Validation Checkpoint

**Files:**
- All files changed in Tasks 1-8.

- [ ] **Step 1: Run static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Expected: all commands pass.

- [ ] **Step 2: Run dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: all commands pass.

- [ ] **Step 3: Run read-only manual display status**

Run:

```powershell
dotnet run --project src/Beacon.DisplayProbe -- status
```

Expected: command prints current physical and virtual topology. It does not modify display state.

- [ ] **Step 4: Sync implementation checkpoint**

Run:

```powershell
git status --short
git add Beacon.slnx src tests docs .github
git commit -m "Implement Windows display lifecycle backend"
git push -u origin codex/milestone-2-display-backend
```

Expected: push succeeds.

### Task 10: Refactor And Revalidate

**Files:**
- Same files touched by Tasks 1-9.

- [ ] **Step 1: Review names and boundaries**

Check:

- `Beacon.Core` contains no Windows namespaces or PInvoke.
- `Beacon.Platform.Windows` contains no server API logic.
- `Beacon.DisplayProbe` contains no policy beyond command parsing and result printing.
- `DisplayLeaseManager` contains the AND cleanup rule.
- `WindowsDisplayBackend` contains no elapsed-time cancellation path.

- [ ] **Step 2: Refactor only after tests are green**

Allowed refactors:

- extract topology predicates into `DisplayTopologySnapshot`,
- extract diagnostic formatting into `WindowsDisplayDiagnostics`,
- simplify fake adapter setup helpers,
- rename records for clarity.

Do not add new behavior in this step.

- [ ] **Step 3: Re-run static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
pnpm --dir src/Beacon.ClientLab lint
pnpm --dir tests/Beacon.ClientLab.Playwright lint
```

Expected: all commands pass.

- [ ] **Step 4: Re-run dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src/Beacon.ClientLab test
pnpm --dir tests/Beacon.ClientLab.Playwright test
```

Expected: all commands pass.

- [ ] **Step 5: Sync refactor checkpoint**

Run:

```powershell
git status --short
git add Beacon.slnx src tests docs .github
git commit -m "Refine Windows display lifecycle boundary"
git push -u origin codex/milestone-2-display-backend
```

Expected: push succeeds.

---

## Self-Review

Spec coverage:

- `REQ-DISP-001` through `REQ-DISP-018` are covered by Tasks 2, 3, 5, 6, 8, and 9.
- `REQ-SESS-006` through `REQ-SESS-008` are covered by Task 2 cleanup tests and Task 5 recovery tests.
- `REQ-MODE-001` through `REQ-MODE-005` are covered by Tasks 3, 8, and 9.
- `REQ-HDR-001` through `REQ-HDR-010` are covered by Tasks 3, 4, and 8.
- `REQ-TEST-001` through `REQ-TEST-010` are covered by fake adapter tests, probe parser tests, and separated manual probe commands.

Placeholder scan:

```powershell
rg -n "TB''D|TO''DO|may''be|implement la''ter|fill in deta''ils|Similar to Ta''sk|appropriate error hand''ling" docs/superpowers/plans/2026-07-07-beacon-stream-milestone-2-display-lifecycle.md
```

Expected: no matches.

Type consistency:

- `DisplayEnsureResult`, `DisplayRestoreResult`, `DisplayRemoveResult`, and `DisplayRecoveryResult` are defined before use.
- `IWindowsDisplayApi`, `DisplayTopologySnapshot`, and `DisplayHdrCapability` are defined before backend tests depend on them.
- Probe command record names match parser tests and `Program.cs` dispatch.
