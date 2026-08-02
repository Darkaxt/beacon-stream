namespace Beacon.Core.Input;

public sealed class NoOpClientInputSink :
    IClientInputSink,
    IClientInputHealthProvider,
    IClientInputSessionLifecycle
{
    private static readonly string[] EventTypes = ["pointer", "keyboard"];
    private static readonly string[] PointerActions = ["move", "down", "up", "tap"];
    private static readonly string[] KeyboardActions = ["down", "up", "press"];

    public Task<ClientInputResult> ForwardAsync(
        ClientInputBatch batch,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(ClientInputResult.Ok(batch.Events.Count));
    }

    public ClientInputHealth GetHealth() =>
        new(
            Ready: true,
            Backend: "no-op",
            Diagnostic: "No-op input sink active for the Beacon test host.",
            SupportedEventTypes: EventTypes,
            SupportedPointerActions: PointerActions,
            SupportedKeyboardActions: KeyboardActions);

    public Task ReleaseSessionAsync(string sessionId, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
