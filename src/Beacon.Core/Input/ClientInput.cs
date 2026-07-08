namespace Beacon.Core.Input;

public sealed record ClientInputEvent(
    string Type,
    string Action,
    int? PointerId = null,
    double? X = null,
    double? Y = null,
    int? Buttons = null,
    string? Key = null,
    string? Code = null,
    double? Value = null);

public sealed record ClientInputBatch(
    string ClientId,
    string SessionId,
    string DisplayId,
    long Sequence,
    IReadOnlyList<ClientInputEvent> Events);

public sealed record ClientInputResult(bool Success, int EventCount, string? Error)
{
    public static ClientInputResult Ok(int eventCount) => new(true, eventCount, null);

    public static ClientInputResult Fail(string error) => new(false, 0, error);
}

public sealed record ClientInputHealth(
    bool Ready,
    string Backend,
    string Diagnostic,
    IReadOnlyList<string> SupportedEventTypes,
    IReadOnlyList<string> SupportedPointerActions,
    IReadOnlyList<string> SupportedKeyboardActions)
{
    public static ClientInputHealth Unknown(string diagnostic) =>
        new(false, "unknown", diagnostic, [], [], []);
}

public interface IClientInputSink
{
    Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken);
}

public interface IClientInputHealthProvider
{
    ClientInputHealth GetHealth();
}

public sealed class NoOpClientInputSink : IClientInputSink, IClientInputHealthProvider
{
    private static readonly string[] EventTypes = ["pointer", "keyboard"];
    private static readonly string[] PointerActions = ["move", "down", "up", "tap"];
    private static readonly string[] KeyboardActions = ["down", "up", "press"];

    public Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientInputResult.Ok(batch.Events.Count));
    }

    public ClientInputHealth GetHealth() =>
        new(
            Ready: true,
            Backend: "no-op",
            Diagnostic: "No-op input sink active for fake host mode.",
            SupportedEventTypes: EventTypes,
            SupportedPointerActions: PointerActions,
            SupportedKeyboardActions: KeyboardActions);
}
