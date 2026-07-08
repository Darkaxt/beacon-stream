using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit.Tests;

public sealed class CockpitShellViewModelTests
{
    [Fact]
    public async Task RefreshPopulatesDashboardState()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot(
            [CreateZFoldClient()],
            [new CockpitSessionSummary("steam-shortcut:3767414131")],
            [new CockpitStreamSummary(
                "z-fold-7-steam-shortcut:3767414131",
                "z-fold-7",
                "steam-shortcut:3767414131",
                "client-z-fold-7",
                "av1",
                120,
                65,
                "lan-direct",
                "running",
                null)],
            [new CockpitOwnershipSummary(
                "z-fold-7-steam-shortcut:3767414131",
                "steam-shortcut:3767414131",
                4321,
                false,
                false,
                false,
                [])],
            new CockpitGameSummary(36, ["Steam library stale"])));
        var viewModel = new CockpitShellViewModel(api);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, viewModel.ClientCount);
        Assert.Equal(1, viewModel.SessionCount);
        Assert.Equal(1, viewModel.StreamCount);
        Assert.Equal(1, viewModel.OwnershipCount);
        Assert.Equal(36, viewModel.GameCount);
        Assert.Contains("z-fold-7", viewModel.Clients);
        Assert.Equal("Z Fold 7", viewModel.ProfileName);
        Assert.Equal(2560, viewModel.ProfilePreferredWidth);
        Assert.Equal(1600, viewModel.ProfilePreferredHeight);
        Assert.Equal(120, viewModel.ProfilePreferredRefreshHz);
        Assert.Equal("virtual-primary", viewModel.ProfileMode);
        Assert.True(viewModel.ProfileRestorePhysicalDisplayOnEnd);
        Assert.True(viewModel.ProfileForbidMirrorMode);
        Assert.Contains("steam-shortcut:3767414131", viewModel.Sessions);
        Assert.Contains("z-fold-7 steam-shortcut:3767414131 running av1 120fps", viewModel.Streams);
        Assert.Contains("steam-shortcut:3767414131 process=False child=False window=False", viewModel.Ownership);
        Assert.Contains("Steam library stale", viewModel.Diagnostics);
    }

    [Fact]
    public async Task SaveSelectedClientProfileDelegatesToServer()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot(
            [CreateZFoldClient()],
            [],
            [],
            [],
            new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api);
        await viewModel.RefreshAsync(CancellationToken.None);

        viewModel.ProfilePreferredRefreshHz = 90;
        viewModel.ProfileMode = "physical-blackout";
        viewModel.ProfileRestorePhysicalDisplayOnEnd = false;
        viewModel.ProfileForbidMirrorMode = false;
        viewModel.ProfileKeepAppRunningOnDisconnect = true;
        viewModel.ProfileAllowEmergencyRestoreFromClient = false;

        await viewModel.SaveSelectedClientProfileAsync(CancellationToken.None);

        Assert.Equal("z-fold-7", api.PatchedClientId);
        Assert.NotNull(api.LastProfilePatch);
        Assert.Equal(90, api.LastProfilePatch.PreferredRefreshHz);
        Assert.Equal("physical-blackout", api.LastProfilePatch.Mode);
        Assert.False(api.LastProfilePatch.RestorePhysicalDisplayOnEnd);
        Assert.False(api.LastProfilePatch.ForbidMirrorMode);
        Assert.True(api.LastProfilePatch.KeepAppRunningOnDisconnect);
        Assert.False(api.LastProfilePatch.AllowEmergencyRestoreFromClient);
        Assert.Equal("Profile saved for z-fold-7.", viewModel.StatusMessage);
    }

    [Fact]
    public async Task RecoveryMethodsDelegateToServer()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], [], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api) { SelectedClientId = "z-fold-7" };

        await viewModel.RestorePhysicalAsync(CancellationToken.None);
        await viewModel.MoveWindowsBackAsync(CancellationToken.None);
        await viewModel.CloseVirtualWindowsAsync(CancellationToken.None);
        await viewModel.TerminateVirtualProcessesAsync(CancellationToken.None);
        await viewModel.RecoverSelectedClientAsync(CancellationToken.None);
        await viewModel.RemoveSelectedClientDisplayLeaseAsync(CancellationToken.None);
        await viewModel.StopSelectedClientStreamAsync(CancellationToken.None);

        Assert.True(api.RestorePhysicalCalled);
        Assert.True(api.MoveWindowsBackCalled);
        Assert.True(api.MoveWindowsBackMinimized);
        Assert.True(api.CloseVirtualWindowsCalled);
        Assert.True(api.TerminateVirtualProcessesCalled);
        Assert.Equal("z-fold-7", api.RecoveredClientId);
        Assert.Equal("z-fold-7", api.RemovedClientDisplayLeaseId);
        Assert.Equal("z-fold-7", api.StoppedClientStreamId);
    }

    [Fact]
    public async Task RecoverSelectedClientReportsMissingSelection()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], [], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api);

        await viewModel.RecoverSelectedClientAsync(CancellationToken.None);

        Assert.Null(api.RecoveredClientId);
        Assert.Equal("Select a client before recovering its display.", viewModel.StatusMessage);
    }

    [Fact]
    public void ConstructorStoresServerUrl()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], [], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api, "http://127.0.0.1:5000");

        Assert.Equal("http://127.0.0.1:5000", viewModel.ServerUrl);
    }

    [Fact]
    public async Task RefreshReportsServerFailures()
    {
        var viewModel = new CockpitShellViewModel(new FailingCockpitApi("server unavailable"));

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal("Refresh failed: server unavailable", viewModel.StatusMessage);
    }

    private sealed class FakeCockpitApi(CockpitSnapshot snapshot) : ICockpitApi
    {
        public bool RestorePhysicalCalled { get; private set; }

        public bool MoveWindowsBackCalled { get; private set; }

        public bool MoveWindowsBackMinimized { get; private set; }

        public bool CloseVirtualWindowsCalled { get; private set; }

        public bool TerminateVirtualProcessesCalled { get; private set; }

        public string? RecoveredClientId { get; private set; }

        public string? RemovedClientDisplayLeaseId { get; private set; }

        public string? StoppedClientStreamId { get; private set; }

        public string? PatchedClientId { get; private set; }

        public CockpitClientProfilePatch? LastProfilePatch { get; private set; }

        public Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public Task PatchClientProfileAsync(string clientId, CockpitClientProfilePatch patch, CancellationToken cancellationToken)
        {
            PatchedClientId = clientId;
            LastProfilePatch = patch;
            return Task.CompletedTask;
        }

        public Task RestorePhysicalAsync(CancellationToken cancellationToken)
        {
            RestorePhysicalCalled = true;
            return Task.CompletedTask;
        }

        public Task MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken)
        {
            MoveWindowsBackCalled = true;
            MoveWindowsBackMinimized = minimize;
            return Task.CompletedTask;
        }

        public Task CloseVirtualWindowsAsync(CancellationToken cancellationToken)
        {
            CloseVirtualWindowsCalled = true;
            return Task.CompletedTask;
        }

        public Task TerminateVirtualProcessesAsync(CancellationToken cancellationToken)
        {
            TerminateVirtualProcessesCalled = true;
            return Task.CompletedTask;
        }

        public Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken)
        {
            RecoveredClientId = clientId;
            return Task.CompletedTask;
        }

        public Task RemoveClientDisplayLeaseAsync(string clientId, CancellationToken cancellationToken)
        {
            RemovedClientDisplayLeaseId = clientId;
            return Task.CompletedTask;
        }

        public Task StopClientStreamAsync(string clientId, CancellationToken cancellationToken)
        {
            StoppedClientStreamId = clientId;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCockpitApi(string message) : ICockpitApi
    {
        public Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromException<CockpitSnapshot>(new InvalidOperationException(message));

        public Task PatchClientProfileAsync(string clientId, CockpitClientProfilePatch patch, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task RestorePhysicalAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task MoveWindowsBackAsync(bool minimize, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task CloseVirtualWindowsAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task TerminateVirtualProcessesAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task RemoveClientDisplayLeaseAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task StopClientStreamAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }

    private static CockpitClientSummary CreateZFoldClient() =>
        new(
            "z-fold-7",
            new CockpitClientProfile(
                "Z Fold 7",
                new CockpitDisplayProfile(2560, 1600, 120, "Prefer", "virtual-primary", true, true),
                new CockpitStreamProfile("auto", "auto", null),
                new CockpitAudioProfile("stereo"),
                new CockpitSessionProfile(false, true)));
}
