namespace Beacon.Core.Benchmarks;

public sealed record BenchmarkTransportPlan(
    int ReliablePacketCount,
    int ReliablePayloadBytes,
    int DatagramPacketCount,
    int DatagramPayloadBytes,
    long MeasurementIntervalUs);
