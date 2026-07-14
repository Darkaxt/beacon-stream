using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Beacon.Core.Benchmarks;

public sealed class FakeBenchmarkRuntime : IBenchmarkRuntime
{
    private readonly ConcurrentDictionary<string, BenchmarkRuntimeState> runtimes =
        new(StringComparer.OrdinalIgnoreCase);
    private long nextRuntimeGeneration;

    public int ActiveListenerPort { get; set; } = 48000;

    public string? NextStartError { get; set; }

    public string? NextStopError { get; set; }

    public List<BenchmarkRuntimePlan> StartCalls { get; } = [];

    public List<string> StopCalls { get; } = [];

    public List<string> Operations { get; } = [];

    public Task<BenchmarkRuntimeStartResult> StartAsync(
        BenchmarkRuntimePlan plan,
        CancellationToken cancellationToken)
    {
        StartCalls.Add(plan);
        Operations.Add($"start:{plan.SessionId}");
        if (!string.IsNullOrWhiteSpace(NextStartError))
        {
            string error = NextStartError;
            NextStartError = null;
            return Task.FromResult(BenchmarkRuntimeStartResult.Fail(error));
        }

        var runtime = new BenchmarkRuntimeState(
            plan.RunId,
            plan.SessionId,
            plan.ClientId.Value,
            plan.Revision,
            plan.SchemaVersion,
            plan.TransportPlan,
            RandomNumberGenerator.GetBytes(16),
            State: "running",
            Error: null,
            ActiveListenerPort,
            CreateRuntimeGeneration());
        runtimes[plan.SessionId] = runtime with { RunToken = (byte[])runtime.RunToken.Clone() };
        return Task.FromResult(BenchmarkRuntimeStartResult.Started(runtime));
    }

    public Task<BenchmarkRuntimeStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken)
    {
        StopCalls.Add(sessionId);
        Operations.Add($"stop:{sessionId}");
        if (!string.IsNullOrWhiteSpace(NextStopError))
        {
            string error = NextStopError;
            NextStopError = null;
            return Task.FromResult(BenchmarkRuntimeStopResult.Fail(error));
        }

        if (!runtimes.TryGetValue(sessionId, out BenchmarkRuntimeState? runtime))
        {
            return Task.FromResult(BenchmarkRuntimeStopResult.Fail(
                $"Benchmark runtime '{sessionId}' is not running."));
        }
        if (runtime.State == "stopped")
        {
            return Task.FromResult(BenchmarkRuntimeStopResult.Stopped(runtime));
        }

        CryptographicOperations.ZeroMemory(runtime.RunToken);
        BenchmarkRuntimeState stopped = runtime with
        {
            RunToken = [],
            State = "stopped",
            ActiveListenerPort = null,
        };
        runtimes[sessionId] = stopped;
        return Task.FromResult(BenchmarkRuntimeStopResult.Stopped(stopped));
    }

    public Task<BenchmarkRuntimeState?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken) =>
        Task.FromResult(runtimes.GetValueOrDefault(sessionId));

    private Guid CreateRuntimeGeneration()
    {
        long generation = Interlocked.Increment(ref nextRuntimeGeneration);
        Span<byte> value = stackalloc byte[16];
        BinaryPrimitives.WriteInt64BigEndian(value[8..], generation);
        return new Guid(value);
    }
}
