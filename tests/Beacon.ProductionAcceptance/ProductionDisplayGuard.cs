namespace Beacon.ProductionAcceptance;

internal enum ProductionDisplayGuardTrigger
{
    AcceptanceExited,
    CompletionSignaled,
    PowerResumed,
    SessionUnlocked,
    EmergencyRequested,
}

internal sealed record ProductionDisplayRecoveryResult(bool Success, string Diagnostic)
{
    public static ProductionDisplayRecoveryResult Verified(string diagnostic) => new(true, diagnostic);

    public static ProductionDisplayRecoveryResult Failed(string diagnostic) => new(false, diagnostic);
}

internal interface IProductionDisplayGuardRuntime
{
    bool AcceptanceRunning { get; }

    Task<ProductionDisplayGuardTrigger> WaitForTriggerAsync(CancellationToken cancellationToken);

    Task TerminateAcceptanceAsync(CancellationToken cancellationToken);

    Task ForcePhysicalOutputAsync(CancellationToken cancellationToken);

    Task<ProductionDisplayRecoveryResult> RecoverAsync(CancellationToken cancellationToken);

    Task WaitForRecoveryHeartbeatAsync(CancellationToken cancellationToken);
}

internal sealed class ProductionDisplayGuard(
    IProductionDisplayGuardRuntime runtime,
    TextWriter output)
{
    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        ProductionDisplayGuardTrigger trigger = await runtime.WaitForTriggerAsync(cancellationToken)
            .ConfigureAwait(false);
        await output.WriteLineAsync($"BEACON_GATE5_DISPLAY_GUARD_TRIGGER {trigger}")
            .ConfigureAwait(false);

        if (RequiresAcceptanceTermination(trigger) && runtime.AcceptanceRunning)
        {
            await runtime.TerminateAcceptanceAsync(cancellationToken).ConfigureAwait(false);
        }

        await runtime.ForcePhysicalOutputAsync(cancellationToken).ConfigureAwait(false);
        while (true)
        {
            ProductionDisplayRecoveryResult recovery = await runtime.RecoverAsync(cancellationToken)
                .ConfigureAwait(false);
            await output.WriteLineAsync(
                $"BEACON_GATE5_DISPLAY_GUARD_RECOVERY success={recovery.Success.ToString().ToLowerInvariant()} " +
                $"diagnostic={recovery.Diagnostic}").ConfigureAwait(false);
            if (recovery.Success)
            {
                return 0;
            }

            await runtime.WaitForRecoveryHeartbeatAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static bool RequiresAcceptanceTermination(ProductionDisplayGuardTrigger trigger) =>
        trigger is ProductionDisplayGuardTrigger.PowerResumed
            or ProductionDisplayGuardTrigger.SessionUnlocked
            or ProductionDisplayGuardTrigger.EmergencyRequested;
}
