namespace Beacon.Core.Clients;

public sealed record TelemetrySnapshot(
    int? RttMs,
    double? PacketLossPercent,
    int? DecoderLoadPercent,
    int? EstimatedBandwidthMbps = null,
    string? WifiBand = null,
    int? BatteryPercent = null,
    string? ThermalState = null);
