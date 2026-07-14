namespace Beacon.Platform.Windows.Displays;

internal sealed class TaskDelaySudoVdaHeartbeatScheduler : ISudoVdaHeartbeatScheduler
{
    public ISudoVdaHeartbeatRegistration Schedule(
        Func<TimeSpan> intervalProvider,
        Func<CancellationToken, ValueTask> heartbeat) =>
        new Registration(intervalProvider, heartbeat);

    private sealed class Registration : ISudoVdaHeartbeatRegistration
    {
        private readonly CancellationTokenSource cancellation = new();
        private readonly Task loop;

        public Registration(
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, ValueTask> heartbeat)
        {
            loop = RunAsync(intervalProvider, heartbeat, cancellation.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await cancellation.CancelAsync();
            await loop;
            cancellation.Dispose();
        }

        private static async Task RunAsync(
            Func<TimeSpan> intervalProvider,
            Func<CancellationToken, ValueTask> heartbeat,
            CancellationToken cancellationToken)
        {
            try
            {
                while (true)
                {
                    TimeSpan interval = intervalProvider();
                    if (interval <= TimeSpan.Zero)
                    {
                        throw new InvalidOperationException("SudoVDA heartbeat interval must be positive.");
                    }

                    await Task.Delay(interval, cancellationToken);
                    await heartbeat(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
        }
    }
}
