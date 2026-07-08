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
    string? Error,
    StreamingConnectionDescriptor? Connection);

public sealed record StreamingConnectionDescriptor(
    string Protocol,
    string? LaunchUri,
    IReadOnlyList<StreamingEndpointDescriptor> Endpoints,
    IReadOnlyDictionary<string, string> Metadata);

public sealed record StreamingEndpointDescriptor(
    string Role,
    string Uri);
