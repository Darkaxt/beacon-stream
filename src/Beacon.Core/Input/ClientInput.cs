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

public interface IClientInputSink
{
    Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken);
}

public sealed class NoOpClientInputSink : IClientInputSink
{
    public Task<ClientInputResult> ForwardAsync(ClientInputBatch batch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientInputResult.Ok(batch.Events.Count));
    }
}
