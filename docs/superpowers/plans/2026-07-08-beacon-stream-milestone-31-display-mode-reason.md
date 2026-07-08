# Milestone 31 Display Mode Reason Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Include the selected display mode rationale in the effective session plan and Client Lab so virtual-primary, extended, blackout, and HDR/SDR decisions are explicit before launch.

**Architecture:** Keep the planner pure and deterministic. `PlannedDisplay.Reason` remains the single display explanation string, but it will combine a mode reason with the existing HDR reason. The server already serializes the full planned display; Client Lab will type and render `display.reason`.

**Tech Stack:** .NET, xUnit planner tests, TypeScript/Vitest Client Lab tests.

---

### Task 1: Planner Display Reason

**Files:**
- Modify: `tests/Beacon.Core.Tests/Sessions/SessionPlannerTests.cs`
- Modify: `src/Beacon.Core/Sessions/SessionPlanner.cs`

- [x] **Step 1: Write the failing planner test**

Add a test near the existing planner display tests:

```csharp
[Fact]
public void DisplayReasonExplainsPhysicalBlackoutModeBeforeLaunch()
{
    ClientProfile profile = ClientProfile.CreateZFold7Default() with
    {
        Display = ClientProfile.CreateZFold7Default().Display with { Mode = "physical-blackout" }
    };

    SessionPlanResult result = SessionPlanner.CreatePlan(
        profile,
        new EndpointCapabilities(Av1: true, Hevc: true, H264: true, Hdr10: true, VirtualDisplayHdrSupported: false, MaxFps: 120),
        new TelemetrySnapshot(RttMs: 8, PacketLossPercent: 0, DecoderLoadPercent: 20, EstimatedBandwidthMbps: 120, WifiBand: "wifi-7"),
        Dispatch);

    SessionPlan plan = Assert.IsType<SessionPlan>(result.Plan);
    Assert.Equal("physical-blackout", plan.Display.Mode);
    Assert.Contains("blackout", plan.Display.Reason, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("HDR disabled", plan.Display.Reason, StringComparison.OrdinalIgnoreCase);
}
```

- [x] **Step 2: Run focused planner tests to verify failure**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter DisplayReasonExplainsPhysicalBlackoutModeBeforeLaunch
```

Expected: FAIL because `PlannedDisplay.Reason` only contains HDR explanation today.

- [x] **Step 3: Implement mode reason composition**

In `SessionPlanner.CreatePlan`, compute:

```csharp
string displayReason = $"{CreateDisplayModeReason(profile.Display.Mode)} {hdrReason}";
```

and pass `displayReason` to `PlannedDisplay`.

Add:

```csharp
private static string CreateDisplayModeReason(string mode)
{
    string normalized = mode.Trim().ToLowerInvariant();
    return normalized switch
    {
        "physical-blackout" => "Display mode physical-blackout selected by server profile policy; physical display recovery remains available.",
        "extended" => "Display mode extended selected by server profile policy.",
        "virtual-primary" => "Display mode virtual-primary selected by server profile policy.",
        "" => "Display mode virtual-primary selected by default server policy.",
        _ => $"Display mode {mode} selected by server profile policy."
    };
}
```

- [x] **Step 4: Run focused planner tests**

Run:

```powershell
dotnet test tests\Beacon.Core.Tests\Beacon.Core.Tests.csproj --filter SessionPlannerTests
```

Expected: PASS.

### Task 2: Docs And Validation

**Files:**
- Modify: `src/Beacon.ClientLab/src/clientLab.ts`
- Modify: `src/Beacon.ClientLab/src/clientLab.test.ts`
- Modify: `tests/Beacon.ClientLab.Playwright/tests/client-lab.spec.ts`
- Modify: `README.md`
- Modify: `docs/superpowers/plans/2026-07-08-beacon-stream-milestone-31-display-mode-reason.md`

- [x] **Step 1: Write failing Client Lab display-reason test**

Update `formats plan details with reason` so `display` includes:

```typescript
reason: 'Display mode physical-blackout selected by server profile policy; physical display recovery remains available. HDR disabled because virtual display does not report HDR capability.'
```

and add:

```typescript
expect(formatPlanDetails(plan)).toContain('physical-blackout selected by server profile policy');
```

Expected: Vitest fails because `formatPlanDetails` does not render the display reason.

- [x] **Step 2: Render display reason in Client Lab**

Add `reason: string` to `PlanResponse.display` and include it in `formatPlanDetails` before the stream reason.

- [x] **Step 3: Document display reason hardening**

Update the summary paragraph to mention Milestone 31 and clarify that the planner display reason includes both mode and HDR/SDR rationale.

- [x] **Step 4: Run full validation**

Run:

```powershell
dotnet format Beacon.slnx --verify-no-changes
dotnet build Beacon.slnx -warnaserror
dotnet test Beacon.slnx
pnpm --dir src\Beacon.ClientLab lint
pnpm --dir src\Beacon.ClientLab test
pnpm --dir tests\Beacon.ClientLab.Playwright lint
pnpm --dir tests\Beacon.ClientLab.Playwright test
& "$env:USERPROFILE\.gradle\wrapper\dists\gradle-8.14.1-bin\baw1sv0jfoi8rxs14qo3h49cs\gradle-8.14.1\bin\gradle.bat" --no-daemon -p src\Beacon.Android test assembleDebug
rg "Thread\.Sleep|Task\.Delay|timeout|Timeout|CancelAfter|CancellationTokenSource\(" src tests -n
```

Expected: all build/test/lint commands pass. The timeout-pattern audit should return no matches.

- [ ] **Step 5: Commit and sync**

Commit, push, open a pull request, wait for CI, and merge only after CI is green.
