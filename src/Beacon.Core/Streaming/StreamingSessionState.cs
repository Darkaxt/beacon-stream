namespace Beacon.Core.Streaming;

public sealed record StreamingSessionState(
    string SessionId,
    string ClientId,
    string AppId,
    string DisplayId,
    string Codec,
    int Fps,
    int InitialBitrateMbps,
    string Transport,
    string State,
    string? Error);
