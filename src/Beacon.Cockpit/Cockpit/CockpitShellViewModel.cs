using System.Collections.ObjectModel;
using System.Windows.Input;

namespace Beacon.Cockpit.Cockpit;

public sealed class CockpitShellViewModel : ObservableObject
{
    private readonly ICockpitApi api;
    private readonly RelayCommand recoverSelectedClientCommand;
    private readonly RelayCommand removeSelectedClientDisplayLeaseCommand;
    private readonly RelayCommand saveSelectedClientProfileCommand;
    private readonly RelayCommand stopSelectedClientStreamCommand;
    private IReadOnlyList<CockpitClientSummary> clientSummaries = [];
    private int clientCount;
    private int sessionCount;
    private int streamCount;
    private int ownershipCount;
    private int gameCount;
    private string selectedClientId = string.Empty;
    private string profileName = string.Empty;
    private int profilePreferredWidth;
    private int profilePreferredHeight;
    private int profilePreferredRefreshHz;
    private string profileHdrPreference = string.Empty;
    private string profileMode = string.Empty;
    private bool profileRestorePhysicalDisplayOnEnd;
    private bool profileForbidMirrorMode;
    private string profileCodecPreference = string.Empty;
    private string profileQualityMode = string.Empty;
    private string profileBitrateCapMbps = string.Empty;
    private string profileAudioMode = string.Empty;
    private bool profileKeepAppRunningOnDisconnect;
    private bool profileAllowEmergencyRestoreFromClient;
    private string statusMessage = "Ready.";

    public CockpitShellViewModel(ICockpitApi api, string serverUrl = "http://localhost:5000")
    {
        this.api = api;
        ServerUrl = serverUrl;
        RefreshCommand = new RelayCommand(() => RefreshAsync(CancellationToken.None));
        RestorePhysicalCommand = new RelayCommand(() => RestorePhysicalAsync(CancellationToken.None));
        MoveWindowsBackCommand = new RelayCommand(() => MoveWindowsBackAsync(CancellationToken.None));
        CloseVirtualWindowsCommand = new RelayCommand(() => CloseVirtualWindowsAsync(CancellationToken.None));
        TerminateVirtualProcessesCommand = new RelayCommand(() => TerminateVirtualProcessesAsync(CancellationToken.None));
        saveSelectedClientProfileCommand = new RelayCommand(
            () => SaveSelectedClientProfileAsync(CancellationToken.None),
            () => !string.IsNullOrWhiteSpace(SelectedClientId));
        SaveSelectedClientProfileCommand = saveSelectedClientProfileCommand;
        recoverSelectedClientCommand = new RelayCommand(
            () => RecoverSelectedClientAsync(CancellationToken.None),
            () => !string.IsNullOrWhiteSpace(SelectedClientId));
        RecoverSelectedClientCommand = recoverSelectedClientCommand;
        removeSelectedClientDisplayLeaseCommand = new RelayCommand(
            () => RemoveSelectedClientDisplayLeaseAsync(CancellationToken.None),
            () => !string.IsNullOrWhiteSpace(SelectedClientId));
        RemoveSelectedClientDisplayLeaseCommand = removeSelectedClientDisplayLeaseCommand;
        stopSelectedClientStreamCommand = new RelayCommand(
            () => StopSelectedClientStreamAsync(CancellationToken.None),
            () => !string.IsNullOrWhiteSpace(SelectedClientId));
        StopSelectedClientStreamCommand = stopSelectedClientStreamCommand;
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

    public int StreamCount
    {
        get => streamCount;
        private set => SetProperty(ref streamCount, value);
    }

    public int OwnershipCount
    {
        get => ownershipCount;
        private set => SetProperty(ref ownershipCount, value);
    }

    public string SelectedClientId
    {
        get => selectedClientId;
        set
        {
            if (SetProperty(ref selectedClientId, value))
            {
                LoadSelectedClientProfile();
                recoverSelectedClientCommand.RaiseCanExecuteChanged();
                removeSelectedClientDisplayLeaseCommand.RaiseCanExecuteChanged();
                saveSelectedClientProfileCommand.RaiseCanExecuteChanged();
                stopSelectedClientStreamCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ProfileName
    {
        get => profileName;
        set => SetProperty(ref profileName, value);
    }

    public int ProfilePreferredWidth
    {
        get => profilePreferredWidth;
        set => SetProperty(ref profilePreferredWidth, value);
    }

    public int ProfilePreferredHeight
    {
        get => profilePreferredHeight;
        set => SetProperty(ref profilePreferredHeight, value);
    }

    public int ProfilePreferredRefreshHz
    {
        get => profilePreferredRefreshHz;
        set => SetProperty(ref profilePreferredRefreshHz, value);
    }

    public string ProfileHdrPreference
    {
        get => profileHdrPreference;
        set => SetProperty(ref profileHdrPreference, value);
    }

    public string ProfileMode
    {
        get => profileMode;
        set => SetProperty(ref profileMode, value);
    }

    public bool ProfileRestorePhysicalDisplayOnEnd
    {
        get => profileRestorePhysicalDisplayOnEnd;
        set => SetProperty(ref profileRestorePhysicalDisplayOnEnd, value);
    }

    public bool ProfileForbidMirrorMode
    {
        get => profileForbidMirrorMode;
        set => SetProperty(ref profileForbidMirrorMode, value);
    }

    public string ProfileCodecPreference
    {
        get => profileCodecPreference;
        set => SetProperty(ref profileCodecPreference, value);
    }

    public string ProfileQualityMode
    {
        get => profileQualityMode;
        set => SetProperty(ref profileQualityMode, value);
    }

    public string ProfileBitrateCapMbps
    {
        get => profileBitrateCapMbps;
        set => SetProperty(ref profileBitrateCapMbps, value);
    }

    public string ProfileAudioMode
    {
        get => profileAudioMode;
        set => SetProperty(ref profileAudioMode, value);
    }

    public bool ProfileKeepAppRunningOnDisconnect
    {
        get => profileKeepAppRunningOnDisconnect;
        set => SetProperty(ref profileKeepAppRunningOnDisconnect, value);
    }

    public bool ProfileAllowEmergencyRestoreFromClient
    {
        get => profileAllowEmergencyRestoreFromClient;
        set => SetProperty(ref profileAllowEmergencyRestoreFromClient, value);
    }

    public string StatusMessage
    {
        get => statusMessage;
        private set => SetProperty(ref statusMessage, value);
    }

    public string ServerUrl { get; }

    public ObservableCollection<string> Clients { get; } = [];

    public ObservableCollection<string> Sessions { get; } = [];

    public ObservableCollection<string> Streams { get; } = [];

    public ObservableCollection<string> Ownership { get; } = [];

    public ObservableCollection<string> Diagnostics { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand RestorePhysicalCommand { get; }

    public ICommand MoveWindowsBackCommand { get; }

    public ICommand CloseVirtualWindowsCommand { get; }

    public ICommand TerminateVirtualProcessesCommand { get; }

    public ICommand RecoverSelectedClientCommand { get; }

    public ICommand RemoveSelectedClientDisplayLeaseCommand { get; }

    public ICommand SaveSelectedClientProfileCommand { get; }

    public ICommand StopSelectedClientStreamCommand { get; }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        CockpitSnapshot snapshot;
        try
        {
            snapshot = await api.GetSnapshotAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Refresh failed: {ex.Message}";
            return;
        }

        clientSummaries = snapshot.Clients;
        Replace(Clients, clientSummaries.Select(client => client.ClientId));
        Replace(Sessions, snapshot.Sessions.Select(session => session.AppId));
        Replace(Streams, snapshot.Streams.Select(stream =>
        {
            string connection = string.IsNullOrWhiteSpace(stream.Connection?.LaunchUri)
                ? "no connection URI"
                : stream.Connection.LaunchUri;
            return $"{stream.ClientId} {stream.AppId} {stream.State} {stream.Codec} {stream.Fps}fps {connection}";
        }));
        Replace(Ownership, snapshot.Ownership.Select(ownership =>
            $"{ownership.AppId} process={ownership.LaunchedProcessRunning} child={ownership.ChildProcessRunning} window={ownership.OwnedWindowRemaining}"));
        Replace(Diagnostics, snapshot.Games.Diagnostics);

        ClientCount = snapshot.Clients.Count;
        SessionCount = snapshot.Sessions.Count;
        StreamCount = snapshot.Streams.Count;
        OwnershipCount = snapshot.Ownership.Count;
        GameCount = snapshot.Games.Total;

        if (Clients.Count == 0)
        {
            SelectedClientId = string.Empty;
            ClearProfileFields();
        }
        else if (string.IsNullOrWhiteSpace(SelectedClientId) || !Clients.Contains(SelectedClientId))
        {
            SelectedClientId = Clients[0];
        }
        else
        {
            LoadSelectedClientProfile();
        }

        StatusMessage = "Snapshot refreshed.";
    }

    public async Task SaveSelectedClientProfileAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedClientId))
        {
            StatusMessage = "Select a client before saving its profile.";
            return;
        }

        if (!TryReadBitrate(out int? bitrateCapMbps))
        {
            StatusMessage = "Bitrate cap must be empty or a whole number.";
            return;
        }

        var patch = new CockpitClientProfilePatch
        {
            PreferredWidth = ProfilePreferredWidth,
            PreferredHeight = ProfilePreferredHeight,
            PreferredRefreshHz = ProfilePreferredRefreshHz,
            HdrPreference = ProfileHdrPreference,
            Mode = ProfileMode,
            RestorePhysicalDisplayOnEnd = ProfileRestorePhysicalDisplayOnEnd,
            ForbidMirrorMode = ProfileForbidMirrorMode,
            CodecPreference = ProfileCodecPreference,
            QualityMode = ProfileQualityMode,
            BitrateCapMbps = bitrateCapMbps,
            AudioMode = ProfileAudioMode,
            KeepAppRunningOnDisconnect = ProfileKeepAppRunningOnDisconnect,
            AllowEmergencyRestoreFromClient = ProfileAllowEmergencyRestoreFromClient
        };

        try
        {
            await api.PatchClientProfileAsync(SelectedClientId, patch, cancellationToken);
            StatusMessage = $"Profile saved for {SelectedClientId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Profile save failed: {ex.Message}";
        }
    }

    public async Task RestorePhysicalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await api.RestorePhysicalAsync(cancellationToken);
            StatusMessage = "Physical display restore requested.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Restore failed: {ex.Message}";
        }
    }

    public async Task MoveWindowsBackAsync(CancellationToken cancellationToken)
    {
        try
        {
            await api.MoveWindowsBackAsync(minimize: true, cancellationToken);
            StatusMessage = "Virtual-display windows moved back and minimized.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Move windows failed: {ex.Message}";
        }
    }

    public async Task CloseVirtualWindowsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await api.CloseVirtualWindowsAsync(cancellationToken);
            StatusMessage = "Close requested for virtual-display windows.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Close windows failed: {ex.Message}";
        }
    }

    public async Task TerminateVirtualProcessesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await api.TerminateVirtualProcessesAsync(cancellationToken);
            StatusMessage = "Terminate requested for virtual-display processes.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Terminate processes failed: {ex.Message}";
        }
    }

    public async Task RecoverSelectedClientAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedClientId))
        {
            StatusMessage = "Select a client before recovering its display.";
            return;
        }

        try
        {
            await api.RecoverClientDisplayAsync(SelectedClientId, cancellationToken);
            StatusMessage = $"Display recovery requested for {SelectedClientId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Recovery failed: {ex.Message}";
        }
    }

    public async Task RemoveSelectedClientDisplayLeaseAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedClientId))
        {
            StatusMessage = "Select a client before removing its display lease.";
            return;
        }

        try
        {
            await api.RemoveClientDisplayLeaseAsync(SelectedClientId, cancellationToken);
            StatusMessage = $"Display lease removal requested for {SelectedClientId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Display lease removal failed: {ex.Message}";
        }
    }

    public async Task StopSelectedClientStreamAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SelectedClientId))
        {
            StatusMessage = "Select a client before stopping its stream.";
            return;
        }

        try
        {
            await api.StopClientStreamAsync(SelectedClientId, cancellationToken);
            StatusMessage = $"Stream stop requested for {SelectedClientId}.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Stream stop failed: {ex.Message}";
        }
    }

    private static void Replace(ObservableCollection<string> collection, IEnumerable<string> values)
    {
        collection.Clear();
        foreach (string value in values)
        {
            collection.Add(value);
        }
    }

    private void LoadSelectedClientProfile()
    {
        CockpitClientSummary? selected = clientSummaries.FirstOrDefault(client =>
            client.ClientId.Equals(SelectedClientId, StringComparison.OrdinalIgnoreCase));
        if (selected is null)
        {
            ClearProfileFields();
            return;
        }

        ProfileName = selected.Profile.Name;
        ProfilePreferredWidth = selected.Profile.Display.PreferredWidth;
        ProfilePreferredHeight = selected.Profile.Display.PreferredHeight;
        ProfilePreferredRefreshHz = selected.Profile.Display.PreferredRefreshHz;
        ProfileHdrPreference = selected.Profile.Display.HdrPreference;
        ProfileMode = selected.Profile.Display.Mode;
        ProfileRestorePhysicalDisplayOnEnd = selected.Profile.Display.RestorePhysicalDisplayOnEnd;
        ProfileForbidMirrorMode = selected.Profile.Display.ForbidMirrorMode;
        ProfileCodecPreference = selected.Profile.Stream.CodecPreference;
        ProfileQualityMode = selected.Profile.Stream.QualityMode;
        ProfileBitrateCapMbps = selected.Profile.Stream.BitrateCapMbps?.ToString() ?? string.Empty;
        ProfileAudioMode = selected.Profile.Audio.Mode;
        ProfileKeepAppRunningOnDisconnect = selected.Profile.Session.KeepAppRunningOnDisconnect;
        ProfileAllowEmergencyRestoreFromClient = selected.Profile.Session.AllowEmergencyRestoreFromClient;
    }

    private void ClearProfileFields()
    {
        ProfileName = string.Empty;
        ProfilePreferredWidth = 0;
        ProfilePreferredHeight = 0;
        ProfilePreferredRefreshHz = 0;
        ProfileHdrPreference = string.Empty;
        ProfileMode = string.Empty;
        ProfileRestorePhysicalDisplayOnEnd = false;
        ProfileForbidMirrorMode = false;
        ProfileCodecPreference = string.Empty;
        ProfileQualityMode = string.Empty;
        ProfileBitrateCapMbps = string.Empty;
        ProfileAudioMode = string.Empty;
        ProfileKeepAppRunningOnDisconnect = false;
        ProfileAllowEmergencyRestoreFromClient = false;
    }

    private bool TryReadBitrate(out int? bitrateCapMbps)
    {
        bitrateCapMbps = null;
        if (string.IsNullOrWhiteSpace(ProfileBitrateCapMbps))
        {
            return true;
        }

        if (!int.TryParse(ProfileBitrateCapMbps, out int parsed))
        {
            return false;
        }

        bitrateCapMbps = parsed;
        return true;
    }
}
