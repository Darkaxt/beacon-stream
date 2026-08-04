using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Beacon.Core.Clients;

namespace Beacon.Core.Benchmarks;

public sealed record BenchmarkRuntimePlan(
    Guid RunId,
    ClientId ClientId,
    BenchmarkTrigger Trigger,
    BenchmarkTransportPlan TransportPlan,
    int SchemaVersion = 1)
{
    public string SessionId => $"benchmark:{RunId:D}";

    public ulong Revision => CreateRevision();

    private ulong CreateRevision()
    {
        using var material = new MemoryStream();
        using (var writer = new BinaryWriter(material, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(RunId.ToByteArray());
            writer.Write(ClientId.Value);
            writer.Write((int)Trigger);
            writer.Write(SchemaVersion);
            writer.Write(TransportPlan.ReliablePacketCount);
            writer.Write(TransportPlan.ReliablePayloadBytes);
            writer.Write(TransportPlan.DatagramPacketCount);
            writer.Write(TransportPlan.DatagramPayloadBytes);
            writer.Write(TransportPlan.MeasurementIntervalUs);
        }

        byte[] digest = SHA256.HashData(material.GetBuffer().AsSpan(0, checked((int)material.Length)));
        ulong revision = BinaryPrimitives.ReadUInt64BigEndian(digest);
        CryptographicOperations.ZeroMemory(digest);
        return revision == 0 ? 1 : revision;
    }
}

public sealed record BenchmarkRuntimeState(
    Guid RunId,
    string SessionId,
    string ClientId,
    ulong PlanRevision,
    int SchemaVersion,
    BenchmarkTransportPlan TransportPlan,
    byte[] RunToken,
    string State,
    string? Error,
    int? ActiveListenerPort,
    Guid RuntimeGeneration);

public sealed record BenchmarkRuntimeStartResult(
    bool Success,
    BenchmarkRuntimeState? Runtime,
    string? Error)
{
    public static BenchmarkRuntimeStartResult Started(BenchmarkRuntimeState runtime) =>
        new(true, runtime, null);

    public static BenchmarkRuntimeStartResult Fail(string error) => new(false, null, error);
}

public sealed record BenchmarkRuntimeStopResult(
    bool Success,
    BenchmarkRuntimeState? Runtime,
    string? Error)
{
    public static BenchmarkRuntimeStopResult Stopped(BenchmarkRuntimeState runtime) =>
        new(true, runtime, null);

    public static BenchmarkRuntimeStopResult Fail(string error) => new(false, null, error);
}

public interface IBenchmarkRuntime
{
    Task<BenchmarkRuntimeStartResult> StartAsync(
        BenchmarkRuntimePlan plan,
        CancellationToken cancellationToken);

    Task<BenchmarkRuntimeStopResult> StopAsync(
        string sessionId,
        CancellationToken cancellationToken);

    Task<BenchmarkRuntimeState?> GetAsync(
        string sessionId,
        CancellationToken cancellationToken);
}
