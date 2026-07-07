using Beacon.Core.Sessions;

namespace Beacon.Core.Games;

public sealed record GameLaunchRequest(GameDescriptor Game, SessionPlan Plan, string DisplayId);

public sealed record GameLaunchState(
    string SessionId,
    string AppId,
    string LaunchType,
    string LaunchCommand,
    int? ProcessId,
    string DisplayId,
    bool Started);

public sealed record GameLaunchResult(bool Success, GameLaunchState? State, string? Error)
{
    public static GameLaunchResult Ok(GameLaunchState state) => new(true, state, null);

    public static GameLaunchResult Fail(string error) => new(false, null, error);
}

public interface IGameLauncher
{
    Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken);
}
