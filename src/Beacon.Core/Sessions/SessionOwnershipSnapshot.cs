using Beacon.Core.Clients;

namespace Beacon.Core.Sessions;

public sealed record SessionOwnershipSnapshot(
    string SessionId,
    ClientId ClientId,
    string AppId,
    int? LaunchedProcessId,
    bool LaunchedProcessRunning,
    bool ChildProcessRunning,
    bool OwnedWindowRemaining,
    IReadOnlyList<string> Reasons)
{
    public bool HasOwnedWork =>
        LaunchedProcessRunning ||
        ChildProcessRunning ||
        OwnedWindowRemaining;
}
