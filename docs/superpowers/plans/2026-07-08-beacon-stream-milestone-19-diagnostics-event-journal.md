# Diagnostics Event Journal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a server-owned diagnostics event journal so display, recovery, streaming, and launch-preflight decisions are visible in `/admin/snapshot` and the WPF cockpit.

**Architecture:** Create a small core diagnostics model and in-memory bounded journal, register it in the server, and inject optional diagnostic sinks into display lease and external streaming boundaries. Admin snapshot will expose recent structured events, and the cockpit will render them alongside existing game-provider diagnostics. This is observability only; it must not change lifecycle decisions or introduce timeouts.

**Tech Stack:** .NET 10, ASP.NET minimal APIs, WPF MVVM, xUnit, existing fake backends.

---

## Requirements Covered

- `REQ-DISP-018`: display topology decisions expose selected display id, resolution, refresh, HDR flag, and reason where available.
- `REQ-REC-008`: diagnostics expose display API failures, driver/display readiness, restore attempts, stream backend failures, and selected recovery actions.
- `REQ-REC-009`: root-cause-oriented events are available through admin/cockpit surfaces instead of only friendlier one-off errors.
- `REQ-TEST-007`: diagnostics remain interface-backed and fake-testable.

## File Map

- Create: `src/Beacon.Core/Diagnostics/DiagnosticEvent.cs`
  - Diagnostic event record, severity constants, sink/source interfaces, bounded in-memory journal.
- Create: `tests/Beacon.Core.Tests/Diagnostics/DiagnosticEventJournalTests.cs`
  - Capacity ordering and metadata preservation tests.
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
  - Register the journal as both sink and source.
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
  - Include recent diagnostics in `/admin/snapshot`.
  - Publish admin recovery action events.
- Modify: `tests/Beacon.Server.Tests/AdminApiTests.cs`
  - Assert diagnostics appear in admin snapshot after recovery actions.
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
  - Publish lease ensure, cleanup, restore, and remove decisions.
- Add or modify: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`
  - Assert cleanup and failure diagnostics with a fake sink.
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
  - Publish preflight/start/stop diagnostics for external wrapper failures and successes.
- Modify: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`
  - Assert manifest rejection and start diagnostics are emitted.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
  - Add `CockpitDiagnosticEvent` and a root `Diagnostics` list to `CockpitSnapshot`.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs`
  - Update fallback snapshot constructor.
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
  - Render root diagnostics plus existing game diagnostics in the existing `Diagnostics` collection.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
  - Assert root diagnostics deserialize.
- Modify: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`
  - Assert cockpit renders operational diagnostics.
- Modify: `README.md`
  - Document diagnostics in admin snapshot and cockpit.
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md`
  - Track execution state.

## Task 1: Core Diagnostic Event Journal

**Files:**
- Create: `src/Beacon.Core/Diagnostics/DiagnosticEvent.cs`
- Test: `tests/Beacon.Core.Tests/Diagnostics/DiagnosticEventJournalTests.cs`

- [x] **Step 1: Write failing journal tests**

Create `tests/Beacon.Core.Tests/Diagnostics/DiagnosticEventJournalTests.cs`:

```csharp
using Beacon.Core.Diagnostics;

namespace Beacon.Core.Tests.Diagnostics;

public sealed class DiagnosticEventJournalTests
{
    [Fact]
    public void JournalReturnsNewestEventsFirstAndRespectsCapacity()
    {
        var journal = new InMemoryDiagnosticEventJournal(capacity: 2);
        journal.Publish(Create("display", "first"));
        journal.Publish(Create("stream", "second"));
        journal.Publish(Create("recovery", "third"));

        IReadOnlyList<DiagnosticEvent> events = journal.GetRecent(10);

        Assert.Equal(["third", "second"], events.Select(evt => evt.Message));
    }

    [Fact]
    public void JournalPreservesContextMetadata()
    {
        var journal = new InMemoryDiagnosticEventJournal(capacity: 10);

        journal.Publish(new DiagnosticEvent(
            "evt-1",
            DateTimeOffset.UnixEpoch,
            DiagnosticSeverity.Warning,
            "display",
            "lease.ensure",
            "Virtual display repair failed.",
            ClientId: "z-fold-7",
            SessionId: "session-1",
            DisplayId: "client-z-fold-7",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["width"] = "2560",
                ["height"] = "1600",
                ["refreshHz"] = "120"
            }));

        DiagnosticEvent evt = Assert.Single(journal.GetRecent(1));

        Assert.Equal("z-fold-7", evt.ClientId);
        Assert.Equal("client-z-fold-7", evt.DisplayId);
        Assert.Equal("2560", evt.Metadata["width"]);
    }

    private static DiagnosticEvent Create(string category, string message) =>
        new(
            $"evt-{message}",
            DateTimeOffset.UnixEpoch,
            DiagnosticSeverity.Information,
            category,
            "test",
            message,
            ClientId: null,
            SessionId: null,
            DisplayId: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DiagnosticEventJournalTests
```

Expected: compile failures for missing diagnostics namespace/types.

- [x] **Step 3: Add diagnostics core model**

Create `src/Beacon.Core/Diagnostics/DiagnosticEvent.cs`:

```csharp
namespace Beacon.Core.Diagnostics;

public static class DiagnosticSeverity
{
    public const string Information = "information";
    public const string Warning = "warning";
    public const string Error = "error";
}

public sealed record DiagnosticEvent(
    string Id,
    DateTimeOffset TimestampUtc,
    string Severity,
    string Category,
    string Operation,
    string Message,
    string? ClientId,
    string? SessionId,
    string? DisplayId,
    IReadOnlyDictionary<string, string> Metadata)
{
    public static DiagnosticEvent Create(
        string severity,
        string category,
        string operation,
        string message,
        string? clientId = null,
        string? sessionId = null,
        string? displayId = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            Guid.NewGuid().ToString("N"),
            DateTimeOffset.UtcNow,
            severity,
            category,
            operation,
            message,
            clientId,
            sessionId,
            displayId,
            metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
}

public interface IDiagnosticEventSink
{
    void Publish(DiagnosticEvent diagnosticEvent);
}

public interface IDiagnosticEventSource
{
    IReadOnlyList<DiagnosticEvent> GetRecent(int count);
}

public sealed class InMemoryDiagnosticEventJournal : IDiagnosticEventSink, IDiagnosticEventSource
{
    private readonly Lock gate = new();
    private readonly Queue<DiagnosticEvent> events = new();
    private readonly int capacity;

    public InMemoryDiagnosticEventJournal(int capacity = 200)
    {
        this.capacity = capacity <= 0 ? 200 : capacity;
    }

    public void Publish(DiagnosticEvent diagnosticEvent)
    {
        lock (gate)
        {
            events.Enqueue(diagnosticEvent);
            while (events.Count > capacity)
            {
                events.Dequeue();
            }
        }
    }

    public IReadOnlyList<DiagnosticEvent> GetRecent(int count)
    {
        int take = count <= 0 ? capacity : count;
        lock (gate)
        {
            return events
                .Reverse()
                .Take(take)
                .ToArray();
        }
    }
}
```

- [x] **Step 4: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DiagnosticEventJournalTests
```

Expected: diagnostic journal tests pass.

- [x] **Step 5: Commit**

Run:

```powershell
git add src/Beacon.Core/Diagnostics/DiagnosticEvent.cs tests/Beacon.Core.Tests/Diagnostics/DiagnosticEventJournalTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Add diagnostics event journal"
git push -u origin codex/milestone-19-diagnostics-event-journal
```

Expected: branch is pushed with the journal checkpoint.

## Task 2: Admin Snapshot Diagnostics

**Files:**
- Modify: `src/Beacon.Server/Hosting/BeaconServiceRegistration.cs`
- Modify: `src/Beacon.Server/Api/AdminEndpoints.cs`
- Test: `tests/Beacon.Server.Tests/AdminApiTests.cs`

- [x] **Step 1: Write failing admin snapshot test**

Add to `AdminApiTests`:

```csharp
[Fact]
public async Task SnapshotIncludesRecentOperationalDiagnostics()
{
    HttpClient client = factory.CreateClient();

    await client.PostAsJsonAsync("/admin/recovery/restore-physical", new { });
    HttpResponseMessage response = await client.GetAsync("/admin/snapshot");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    using JsonDocument document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
    JsonElement diagnostic = Assert.Single(document.RootElement.GetProperty("diagnostics").EnumerateArray());

    Assert.Equal("recovery", diagnostic.GetProperty("category").GetString());
    Assert.Equal("restore-physical", diagnostic.GetProperty("operation").GetString());
    Assert.Contains("Physical display restore", diagnostic.GetProperty("message").GetString(), StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter SnapshotIncludesRecentOperationalDiagnostics
```

Expected: fail because `/admin/snapshot` has no root `diagnostics` field and recovery actions do not publish events.

- [x] **Step 3: Register diagnostics journal**

In `BeaconServiceRegistration.AddBeaconServices`, after session store registration, add:

```csharp
services.AddSingleton<InMemoryDiagnosticEventJournal>();
services.AddSingleton<IDiagnosticEventSink>(sp => sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
services.AddSingleton<IDiagnosticEventSource>(sp => sp.GetRequiredService<InMemoryDiagnosticEventJournal>());
```

Add `using Beacon.Core.Diagnostics;` at the top.

- [x] **Step 4: Add diagnostics to admin snapshot and recovery restore**

In `AdminEndpoints.MapGet("/snapshot", ...)`, inject `IDiagnosticEventSource diagnostics` and add this root property to the response:

```csharp
diagnostics = diagnostics.GetRecent(100),
```

In `/admin/recovery/restore-physical`, inject `IDiagnosticEventSink diagnostics` and publish after the backend call:

```csharp
DisplayRestoreResult result = await displayBackend.RestorePhysicalPrimaryAsync(cancellationToken);
diagnostics.Publish(DiagnosticEvent.Create(
    result.Success ? DiagnosticSeverity.Information : DiagnosticSeverity.Error,
    "recovery",
    "restore-physical",
    result.Success
        ? "Physical display restore requested."
        : $"Physical display restore failed: {result.Error}"));
return Results.Ok(new { restoreRequested = true });
```

The route keeps the existing success shape for compatibility.

- [x] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Server.Tests/Beacon.Server.Tests.csproj --filter AdminApiTests
```

Expected: all admin API tests pass.

- [x] **Step 6: Commit**

Run:

```powershell
git add src/Beacon.Server/Hosting/BeaconServiceRegistration.cs src/Beacon.Server/Api/AdminEndpoints.cs tests/Beacon.Server.Tests/AdminApiTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Expose diagnostics in admin snapshot"
git push
```

Expected: branch contains the admin diagnostics checkpoint.

## Task 3: Display Lease Diagnostics

**Files:**
- Modify: `src/Beacon.Core/Displays/DisplayLeaseManager.cs`
- Test: `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs`

- [x] **Step 1: Write failing display diagnostics tests**

Create or extend `tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs` with:

```csharp
using Beacon.Core.Clients;
using Beacon.Core.Diagnostics;
using Beacon.Core.Displays;

namespace Beacon.Core.Tests.Displays;

public sealed class DisplayLeaseManagerTests
{
    [Fact]
    public async Task EnsureLeasePublishesFailureDiagnosticBeforeRefusingPhysicalFallback()
    {
        var display = new FakeDisplayBackend { NextEnsureError = "SudoVDA driver not ready" };
        var sink = new RecordingDiagnosticSink();
        var manager = new DisplayLeaseManager(display, sink);

        DisplayLeaseResult result = await manager.EnsureLeaseAsync(CreateProfile(), CancellationToken.None);

        Assert.False(result.Success);
        DiagnosticEvent evt = Assert.Single(sink.Events);
        Assert.Equal("display", evt.Category);
        Assert.Equal("lease.ensure", evt.Operation);
        Assert.Equal("error", evt.Severity);
        Assert.Equal("client-z-fold-7", evt.DisplayId);
        Assert.Equal("2560", evt.Metadata["width"]);
        Assert.Contains("SudoVDA driver not ready", evt.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CleanupPublishesOwnedWorkRetentionDiagnostic()
    {
        var display = new FakeDisplayBackend();
        var sink = new RecordingDiagnosticSink();
        var manager = new DisplayLeaseManager(display, sink);

        bool removed = await manager.CleanupIfAllowedAsync(
            "client-z-fold-7",
            clientActive: false,
            ownedProcessRunning: true,
            ownedWindowRemaining: false,
            CancellationToken.None);

        Assert.False(removed);
        Assert.Contains(sink.Events, evt =>
            evt.Operation == "lease.cleanup.retained" &&
            evt.Metadata["ownedProcessRunning"] == "true");
    }

    private static ClientProfile CreateProfile() => ClientProfile.Default(new ClientId("z-fold-7"), "Z Fold 7");

    private sealed class RecordingDiagnosticSink : IDiagnosticEventSink
    {
        public List<DiagnosticEvent> Events { get; } = [];

        public void Publish(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
    }
}
```

If `DisplayLeaseManagerTests` already exists, add only the two tests and local fake/sink pieces needed.

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: compile failure or assertion failure because `DisplayLeaseManager` does not accept/publish diagnostics.

- [x] **Step 3: Add optional diagnostic sink to display manager**

Change constructor:

```csharp
public sealed class DisplayLeaseManager(IDisplayBackend displayBackend, IDiagnosticEventSink? diagnostics = null)
```

Add `using Beacon.Core.Diagnostics;`.

Publish these events:

- `display` / `lease.ensure` / `information` on successful lease creation.
- `display` / `lease.ensure` / `error` when ensure fails, with width/height/refreshHz/hdrPreference metadata.
- `display` / `lease.cleanup.retained` / `information` when cleanup is blocked by active client or owned work.
- `display` / `lease.cleanup.restore-failed` / `error` when physical-primary restore fails.
- `display` / `lease.cleanup.removed` / `information` when remove succeeds.
- `display` / `lease.recover` / `information|error` during manual recovery.

Use metadata values as strings:

```csharp
new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["width"] = profile.Display.PreferredWidth.ToString(CultureInfo.InvariantCulture),
    ["height"] = profile.Display.PreferredHeight.ToString(CultureInfo.InvariantCulture),
    ["refreshHz"] = profile.Display.PreferredRefreshHz.ToString(CultureInfo.InvariantCulture),
    ["hdrPreference"] = profile.Display.HdrPreference.ToString()
}
```

- [x] **Step 4: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Core.Tests/Beacon.Core.Tests.csproj --filter DisplayLeaseManagerTests
```

Expected: display diagnostics tests pass.

- [x] **Step 5: Commit**

Run:

```powershell
git add src/Beacon.Core/Displays/DisplayLeaseManager.cs tests/Beacon.Core.Tests/Displays/DisplayLeaseManagerTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Publish display lease diagnostics"
git push
```

Expected: display diagnostics checkpoint is synced.

## Task 4: External Streaming Diagnostics

**Files:**
- Modify: `src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs`
- Test: `tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs`

- [x] **Step 1: Write failing streaming diagnostics test**

Add to `ExternalProcessStreamingBackendTests`:

```csharp
[Fact]
public async Task PreflightFailurePublishesDiagnostic()
{
    var sink = new RecordingDiagnosticSink();
    var backend = new ExternalProcessStreamingBackend(
        new ExternalProcessStreamingOptions("C:\\Tools\\missing-wrapper.exe"),
        new FakeExternalStreamingProcessRunner(),
        manifestReader: null,
        sink);

    StreamingPreflightResult result = await backend.CheckReadinessAsync(CreatePlan(), CancellationToken.None);

    Assert.False(result.Success);
    DiagnosticEvent evt = Assert.Single(sink.Events);
    Assert.Equal("streaming", evt.Category);
    Assert.Equal("preflight", evt.Operation);
    Assert.Equal("error", evt.Severity);
    Assert.Equal("z-fold-7-steam-shortcut:3767414131", evt.SessionId);
    Assert.Contains("missing-wrapper.exe", evt.Message, StringComparison.OrdinalIgnoreCase);
}
```

Add this local fake:

```csharp
private sealed class RecordingDiagnosticSink : IDiagnosticEventSink
{
    public List<DiagnosticEvent> Events { get; } = [];

    public void Publish(DiagnosticEvent diagnosticEvent) => Events.Add(diagnosticEvent);
}
```

Add `using Beacon.Core.Diagnostics;`.

- [x] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter PreflightFailurePublishesDiagnostic
```

Expected: compile failure because `ExternalProcessStreamingBackend` does not accept the diagnostic sink.

- [x] **Step 3: Publish external streaming diagnostics**

Change constructor:

```csharp
public sealed class ExternalProcessStreamingBackend(
    ExternalProcessStreamingOptions options,
    IExternalStreamingProcessRunner runner,
    IExternalStreamingManifestReader? manifestReader = null,
    IDiagnosticEventSink? diagnostics = null) : IStreamingBackend
```

Add `using Beacon.Core.Diagnostics;`.

Add a helper:

```csharp
private void Publish(SessionPlan plan, string operation, string severity, string message, IReadOnlyDictionary<string, string>? metadata = null) =>
    diagnostics?.Publish(DiagnosticEvent.Create(
        severity,
        "streaming",
        operation,
        message,
        plan.ClientId.Value,
        plan.SessionId,
        plan.Display.DisplayId,
        metadata));
```

Call it for:

- `preflight` success and failure.
- `start` success and start exception.
- `stop` success and stop failure.

Keep existing return/error behavior unchanged.

- [x] **Step 4: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Platform.Windows.Tests/Beacon.Platform.Windows.Tests.csproj --filter ExternalProcessStreamingBackendTests
```

Expected: all external streaming backend tests pass.

- [x] **Step 5: Commit**

Run:

```powershell
git add src/Beacon.Platform.Windows/Streaming/ExternalProcessStreamingBackend.cs tests/Beacon.Platform.Windows.Tests/Streaming/ExternalProcessStreamingBackendTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Publish external streaming diagnostics"
git push
```

Expected: streaming diagnostics checkpoint is synced.

## Task 5: Cockpit Diagnostics Rendering

**Files:**
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitModels.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs`
- Modify: `src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs`
- Test: `tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs`
- Test: `tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs`

- [ ] **Step 1: Write failing cockpit tests**

In `CockpitApiClientTests`, extend or add a snapshot deserialization test with JSON containing:

```json
"diagnostics": [
  {
    "id": "evt-1",
    "timestampUtc": "1970-01-01T00:00:00+00:00",
    "severity": "error",
    "category": "streaming",
    "operation": "preflight",
    "message": "External streaming manifest codec av1 is not supported.",
    "clientId": "z-fold-7",
    "sessionId": "session-1",
    "displayId": "client-z-fold-7",
    "metadata": { "codec": "av1" }
  }
]
```

Assert:

```csharp
Assert.Single(snapshot.Diagnostics);
Assert.Equal("streaming", snapshot.Diagnostics[0].Category);
Assert.Equal("av1", snapshot.Diagnostics[0].Metadata["codec"]);
```

In `CockpitShellViewModelTests`, add:

```csharp
[Fact]
public async Task RefreshRendersOperationalDiagnosticsBeforeGameProviderDiagnostics()
{
    var api = new FakeCockpitApi
    {
        Snapshot = TestSnapshots.WithDiagnostics(
            new CockpitDiagnosticEvent(
                "evt-1",
                DateTimeOffset.UnixEpoch,
                "error",
                "streaming",
                "preflight",
                "External streaming manifest codec av1 is not supported.",
                "z-fold-7",
                "session-1",
                "client-z-fold-7",
                new Dictionary<string, string>()))
    };
    var viewModel = new CockpitShellViewModel(api);

    await viewModel.RefreshAsync(CancellationToken.None);

    Assert.Contains(viewModel.Diagnostics, value => value.Contains("streaming/preflight", StringComparison.OrdinalIgnoreCase));
    Assert.Contains(viewModel.Diagnostics, value => value.Contains("Steam library stale", StringComparison.OrdinalIgnoreCase));
}
```

Adjust helper names to match the existing test helper style.

- [ ] **Step 2: Verify red**

Run:

```powershell
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj --filter "CockpitApiClientTests|CockpitShellViewModelTests"
```

Expected: compile failures for missing `CockpitDiagnosticEvent` and snapshot property.

- [ ] **Step 3: Add cockpit diagnostic DTOs**

In `CockpitModels.cs`, change snapshot to:

```csharp
public sealed record CockpitSnapshot(
    IReadOnlyList<CockpitClientSummary> Clients,
    IReadOnlyList<CockpitSessionSummary> Sessions,
    IReadOnlyList<CockpitStreamSummary> Streams,
    IReadOnlyList<CockpitOwnershipSummary> Ownership,
    CockpitGameSummary Games,
    IReadOnlyList<CockpitDiagnosticEvent> Diagnostics);
```

Add:

```csharp
public sealed record CockpitDiagnosticEvent(
    string Id,
    DateTimeOffset TimestampUtc,
    string Severity,
    string Category,
    string Operation,
    string Message,
    string? ClientId,
    string? SessionId,
    string? DisplayId,
    IReadOnlyDictionary<string, string> Metadata);
```

Update `CockpitApiClient` fallback:

```csharp
return snapshot ?? new CockpitSnapshot([], [], [], [], new CockpitGameSummary(0, []), []);
```

- [ ] **Step 4: Render diagnostics in view model**

In `CockpitShellViewModel.RefreshAsync`, replace the diagnostics binding line:

```csharp
Replace(Diagnostics, snapshot.Games.Diagnostics);
```

with:

```csharp
Replace(Diagnostics, snapshot.Diagnostics
    .Select(evt => $"[{evt.Severity}] {evt.Category}/{evt.Operation}: {evt.Message}")
    .Concat(snapshot.Games.Diagnostics.Select(message => $"[provider] {message}")));
```

- [ ] **Step 5: Verify green**

Run:

```powershell
dotnet test tests/Beacon.Cockpit.Tests/Beacon.Cockpit.Tests.csproj --filter "CockpitApiClientTests|CockpitShellViewModelTests"
```

Expected: cockpit tests pass.

- [ ] **Step 6: Commit**

Run:

```powershell
git add src/Beacon.Cockpit/Cockpit/CockpitModels.cs src/Beacon.Cockpit/Cockpit/CockpitApiClient.cs src/Beacon.Cockpit/Cockpit/CockpitShellViewModel.cs tests/Beacon.Cockpit.Tests/CockpitApiClientTests.cs tests/Beacon.Cockpit.Tests/CockpitShellViewModelTests.cs docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Show operational diagnostics in cockpit"
git push
```

Expected: cockpit diagnostics checkpoint is synced.

## Task 6: Docs, Validation, And Sync

**Files:**
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md`

- [ ] **Step 1: Update docs**

Add to `README.md` under Recovery actions:

```markdown
`/admin/snapshot` also returns recent operational diagnostics. These events include display lease decisions, physical-primary restore attempts, recovery actions, and streaming preflight/start/stop failures. The WPF cockpit shows them in the Diagnostics tab together with game-provider diagnostics.
```

- [ ] **Step 2: Static validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
```

Expected: both commands pass.

- [ ] **Step 3: Dynamic validation**

Run:

```powershell
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
```

Expected: all commands pass; the Gradle 9 deprecation warning remains acceptable if the command exits successfully.

- [ ] **Step 4: Boundary audit**

Run:

```powershell
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: no matches; diagnostics must not introduce timeout-based lifecycle behavior.

- [ ] **Step 5: Commit and sync**

Run:

```powershell
git add README.md docs/superpowers/plans/2026-07-08-beacon-stream-milestone-19-diagnostics-event-journal.md
git commit -m "Document diagnostics event journal"
git push
gh pr create --draft --base main --head codex/milestone-19-diagnostics-event-journal --title "Add diagnostics event journal" --body "Milestone 19 diagnostics event journal implementation."
gh pr checks 19 --watch
gh pr ready 19
gh pr merge 19 --merge --delete-branch
```

Expected: PR checks pass, PR merges, and local `main` is clean.

## Non-Goals

- No persistent log database.
- No log-file tailing.
- No telemetry aggregation dashboard.
- No display lifecycle behavior changes.
- No retry loops or timeout-based cancellation.
- No copied Sunshine/Apollo-family logging code.
