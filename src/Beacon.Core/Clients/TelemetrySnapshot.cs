namespace Beacon.Core.Clients;

public sealed record TelemetrySnapshot(int RttMs, double PacketLossPercent, int? DecoderLoadPercent);
