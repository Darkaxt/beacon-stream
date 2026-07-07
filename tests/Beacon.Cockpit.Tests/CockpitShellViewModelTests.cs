using Beacon.Cockpit.Cockpit;

namespace Beacon.Cockpit.Tests;

public sealed class CockpitShellViewModelTests
{
    [Fact]
    public async Task RefreshPopulatesDashboardState()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot(
            [new CockpitClientSummary("z-fold-7")],
            [new CockpitSessionSummary("steam-shortcut:3767414131")],
            new CockpitGameSummary(36, ["Steam library stale"])));
        var viewModel = new CockpitShellViewModel(api);

        await viewModel.RefreshAsync(CancellationToken.None);

        Assert.Equal(1, viewModel.ClientCount);
        Assert.Equal(1, viewModel.SessionCount);
        Assert.Equal(36, viewModel.GameCount);
        Assert.Contains("z-fold-7", viewModel.Clients);
        Assert.Contains("steam-shortcut:3767414131", viewModel.Sessions);
        Assert.Contains("Steam library stale", viewModel.Diagnostics);
    }

    [Fact]
    public async Task RecoveryMethodsDelegateToServer()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api) { SelectedClientId = "z-fold-7" };

        await viewModel.RestorePhysicalAsync(CancellationToken.None);
        await viewModel.RecoverSelectedClientAsync(CancellationToken.None);

        Assert.True(api.RestorePhysicalCalled);
        Assert.Equal("z-fold-7", api.RecoveredClientId);
    }

    [Fact]
    public async Task RecoverSelectedClientReportsMissingSelection()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], new CockpitGameSummary(0, [])));
        var viewModel = new CockpitShellViewModel(api);

        await viewModel.RecoverSelectedClientAsync(CancellationToken.None);

        Assert.Null(api.RecoveredClientId);
        Assert.Equal("Select a client before recovering its display.", viewModel.StatusMessage);
    }

    [Fact]
    public void ConstructorStoresServerUrl()
    {
        var api = new FakeCockpitApi(new CockpitSnapshot([], [], new CockpitGameSummary(0, [])));
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

        public string? RecoveredClientId { get; private set; }

        public Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) => Task.FromResult(snapshot);

        public Task RestorePhysicalAsync(CancellationToken cancellationToken)
        {
            RestorePhysicalCalled = true;
            return Task.CompletedTask;
        }

        public Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken)
        {
            RecoveredClientId = clientId;
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCockpitApi(string message) : ICockpitApi
    {
        public Task<CockpitSnapshot> GetSnapshotAsync(CancellationToken cancellationToken) =>
            Task.FromException<CockpitSnapshot>(new InvalidOperationException(message));

        public Task RestorePhysicalAsync(CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));

        public Task RecoverClientDisplayAsync(string clientId, CancellationToken cancellationToken) =>
            Task.FromException(new InvalidOperationException(message));
    }
}
