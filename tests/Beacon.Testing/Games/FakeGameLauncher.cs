namespace Beacon.Core.Games;

public sealed class FakeGameLauncher : IGameLauncher
{
    public string? NextError { get; set; }

    public int? NextProcessId { get; set; }

    public List<GameLaunchRequest> Requests { get; } = [];

    public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);

        if (!string.IsNullOrWhiteSpace(NextError))
        {
            return Task.FromResult(GameLaunchResult.Fail(NextError));
        }

        var state = new GameLaunchState(
            request.Plan.SessionId,
            request.Game.Id,
            request.Game.Launch.Type,
            request.Game.Launch.Command,
            NextProcessId,
            request.DisplayId,
            Started: true);

        return Task.FromResult(GameLaunchResult.Ok(state));
    }
}
