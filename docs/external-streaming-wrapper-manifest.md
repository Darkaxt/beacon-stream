# External Streaming Wrapper Manifest

Beacon external-process streaming uses a small JSON manifest to describe what a configured wrapper can do before Beacon creates a display, launches a game, or asks the client to connect. The manifest is Beacon-owned: it is an adapter contract for Sunshine-compatible or Apollo-compatible wrapper processes, not copied upstream source.

## Configuration

Set one of these values when `BEACON_STREAMING_BACKEND=external-process`:

```powershell
$env:BEACON_EXTERNAL_STREAMING_MANIFEST='C:\Tools\beacon-streaming.json'
```

or:

```json
{
  "Beacon": {
    "Streaming": {
      "ExternalProcess": {
        "ManifestPath": "C:\\Tools\\beacon-streaming.json"
      }
    }
  }
}
```

Beacon passes the same path to the wrapper as `BEACON_WRAPPER_MANIFEST_PATH`.

Beacon launches the wrapper process with the executable directory as `WorkingDirectory`. Wrapper-relative config, logs, or helper files should be resolved from there or from explicit paths passed through arguments, environment variables, or the manifest.

## Example

The checked example is parsed by the Windows manifest reader test so docs and runtime deserialization cannot drift:

- `docs/examples/external-streaming-manifest.example.json`
- `docs/examples/external-streaming-runtime-session.example.json`

## Fields

| Field | Type | Meaning |
| --- | --- | --- |
| `name` | string | Human-readable wrapper name shown in health diagnostics. |
| `protocol` | string | Connection protocol advertised to the client when explicit Beacon connection settings do not override it. |
| `launchUri` | string | URI the thin client can launch after Beacon starts the stream session. |
| `endpoints` | object | Optional named endpoint map, such as `rtsp` and `input`, used in the stream connection descriptor. |
| `codecs` | string array | Supported stream codecs. When present, preflight rejects plans whose codec is absent. |
| `maxFps` | number | Maximum FPS supported by the wrapper. Values less than or equal to zero are treated as unspecified. |
| `maxBitrateMbps` | number | Maximum initial bitrate supported by the wrapper. Values less than or equal to zero are treated as unspecified. |
| `hdr10` | boolean | Whether the wrapper can support HDR10 plans. Beacon reports this truthfully and rejects HDR-required plans when false. |
| `transports` | string array | Supported transport policy names. When present, preflight rejects plans whose transport is absent. |
| `encoders` | string array | Informational encoder list for health UI and diagnostics. |
| `capture` | string array | Informational capture-path list for health UI and diagnostics. |
| `diagnostics` | string array | Wrapper-supplied status notes surfaced in streaming health. |

## Precedence

Explicit Beacon connection settings win over manifest connection fields:

- `BEACON_EXTERNAL_STREAMING_CONNECTION_PROTOCOL`
- `BEACON_EXTERNAL_STREAMING_CONNECTION_LAUNCH_URI`
- `Beacon:Streaming:ExternalProcess:Connection:Endpoints:*`

When those settings are absent, `protocol`, `launchUri`, and `endpoints` from the manifest become the session connection descriptor.

## Runtime Session Descriptor

The manifest describes wrapper capability before any side effects. A started wrapper can also publish actual per-session connection data by writing JSON to the path Beacon passes as:

- environment: `BEACON_STREAM_SESSION_DESCRIPTOR_PATH`
- command argument: `--stream-session-descriptor "<path>"`

Beacon prepares that path before wrapper launch and deletes any stale descriptor for the same session id. The wrapper may write the descriptor immediately or later; Beacon reads it on demand when session state or health is queried. There is no polling loop and no timeout-based wait for the file.

Runtime descriptor fields:

| Field | Type | Meaning |
| --- | --- | --- |
| `protocol` | string | Actual protocol the client should use for this running session. |
| `launchUri` | string | Actual URI the thin client can launch for this running session. |
| `endpoints` | object | Actual named endpoint map, such as `rtsp` and `input`. |
| `metadata` | object | Optional string metadata surfaced in the stream connection descriptor. |
| `diagnostics` | string array | Wrapper-supplied runtime status notes for diagnostics. |

Once present, the runtime session descriptor is treated as evidence from the started wrapper and takes precedence over static manifest connection fields. Explicit Beacon connection settings remain the fallback when no runtime descriptor has been written.

Wrappers with stable connection details should advertise those details through explicit Beacon connection settings or manifest fields. That gives the client an immediate descriptor after launch while the runtime session descriptor can still refine or prove the running wrapper state later. Beacon intentionally does not wait on this file with a timeout-based launch gate.

`Beacon.StreamingProbe` is the checked no-phone producer for this format. It writes the descriptor and can either exit with `--once` for standalone validation or stay alive until Beacon stops the wrapper process.

## Stability Rules

- Manifest validation happens during streaming preflight, before display or launch side effects.
- The manifest is read on demand; there is no polling loop or timeout-based cancellation.
- Beacon treats `diagnostics` and wrapper stdout/stderr as evidence, not as a protocol to parse.
- The manifest reports capability only. It does not make a non-HDR Windows display path HDR-capable by itself.
- Runtime session descriptors are generated state. Beacon clears stale same-session descriptors before wrapper start and removes the descriptor when the stream stops or exits.
