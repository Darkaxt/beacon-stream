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

## Example

The checked example is parsed by the Windows manifest reader test so docs and runtime deserialization cannot drift:

- `docs/examples/external-streaming-manifest.example.json`

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

## Stability Rules

- Manifest validation happens during streaming preflight, before display or launch side effects.
- The manifest is read on demand; there is no polling loop or timeout-based cancellation.
- Beacon treats `diagnostics` and wrapper stdout/stderr as evidence, not as a protocol to parse.
- The manifest reports capability only. It does not make a non-HDR Windows display path HDR-capable by itself.
