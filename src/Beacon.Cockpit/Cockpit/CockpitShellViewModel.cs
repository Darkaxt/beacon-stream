using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Beacon.Cockpit.Cockpit;

public sealed class CockpitShellViewModel : ObservableObject
{
    private readonly ICockpitApi api;
    private readonly RelayCommand recoverSelectedClientCommand;
    private int clientCount;
    private int sessionCount;
    private int gameCount;
    private string selectedClientId = string.Empty;
    private string statusMessage = "Ready.";

    public CockpitShellViewModel(ICockpitApi api)
    {
        this.api = api;
        RefreshCommand = new RelayCommand(() => RefreshAsync(CancellationToken.None));
        RestorePhysicalCommand = new RelayCommand(() => RestorePhysicalAsync(CancellationToken.None));
        recoverSelectedClientCommand = new RelayCommand(
            () => RecoverSelectedClientAsync(CancellationToken.None),
            () => !string.IsNullOrWhiteSpace(SelectedClientId));
        RecoverSelectedClientCommand = recoverSelectedClientCommand;
    }

    public int ClientCount
    {
        get => clientCount;
        private set => SetProperty(ref clientCount, value);
    }

    public int SessionCount
    {
        get => sessionCount;
        private set => SetProperty(ref sessionCount, value);
    }

    public int GameCount
    {
        get => gameCount;
        private set => SetProperty(ref gameCount, value);
    }

    public string SelectedClientId
    {
        get => selectedClientId;
        set
        {
            if (SetProperty(ref selectedClientId, value))
            {
                recoverSelectedClientCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public ObservableCollection<string> Clients { get; } = [];

    public ObservableCollection<string> Sessions { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand RestorePhysicalCommand { get; }

    public ICommand RecoverSelectedClientCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        CockpitSnapshot snapshot = await api.GetSnapshotAsync(cancellationToken);

        Replace(Clients, snapshot.Clients.Select(client => client.ClientId));
        Replace(Sessions, snapshot.Sessions.Select(session => session.AppId));
        Replace(Diagnostics, snapshot.Games.Diagnostics);

        ClientCount = snapshot.Clients.Count;
        SessionCount = snapshot.Sessions.Count;
        GameCount = snapshot.Games.Total;

        if (string.IsNullOrWhiteSpace(SelectedClientId) && Clients.Count > 0)
        {
            SelectedClientId = Clients[0];
        }

        StatusMessage = "Snapshot refreshed.";
    }

    public async Task RestorePhysicalAsync(CancellationToken cancellationToken)
    {
        await api.RestorePhysicalAsync(cancellationToken);
        StatusMessage = "Physical display restore requested.";
    }

    public async Task RecoverSelectedClientAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedClientId))
        {
            StatusMessage = "Select a client before recovering its display.";
            return;
        }

        await api.RecoverClientDisplayAsync(SelectedClientId, cancellationToken);
        StatusMessage = $"Display recovery requested for {SelectedClientId}.";
    }

    private static void Replace(ObservableCollection<string> collection, IEnumerable<string> values)
    {
        collection.Clear();
        foreach (string value in values)
        {
            collection.Add(value);
        }
    }
}
