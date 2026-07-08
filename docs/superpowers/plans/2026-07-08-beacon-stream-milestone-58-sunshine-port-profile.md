# Milestone 58: Sunshine Port Profile

Derive stable Sunshine/GameStream endpoint metadata from one server-owned host and base port, while preserving explicit endpoint overrides and runtime wrapper evidence.

## Requirements

- `REQ-NET-006`: derive Sunshine/GameStream endpoint metadata from a server-owned host plus base-port profile without inventing a launch URI.
- `REQ-CTRL-001`: server remains the source of truth for desired state.
- `REQ-TEST-001`: most validation must not require the real phone.
- `REQ-TEST-007`: streaming integration remains behind fakeable interfaces.
- `REQ-SYNC-006`: docs, implementation, and validation status must stay aligned.

## Source Reference

- LizardByte Sunshine advanced usage documents the network port family: default base port `47989`, HTTPS `-5`, HTTP `0`, web `+1`, RTSP `+21`, video `+9`, control `+10`, audio `+11`, and mic `+13`.
- Reference: https://docs.lizardbyte.dev/projects/sunshine/v0.23.0/about/advanced_usage.html#port
- Beacon copies no Sunshine source for this milestone; it records public configuration offsets as adapter metadata.

## Implementation

- Add `SunshineEndpointProfile` in the Windows streaming boundary.
- Support `Beacon:Streaming:ExternalProcess:Connection:Sunshine:Host` and `Beacon:Streaming:ExternalProcess:Connection:Sunshine:BasePort`.
- Support environment overrides:
  - `BEACON_EXTERNAL_STREAMING_SUNSHINE_HOST`
  - `BEACON_EXTERNAL_STREAMING_SUNSHINE_BASE_PORT`
- Derive endpoint roles: `https`, `http`, `web`, `rtsp`, `video`, `control`, `audio`, and `mic`.
- Advertise `gamestream` when the profile is configured and no explicit protocol is set.
- Merge explicit `Connection:Endpoints:*` values over derived endpoints by role.
- Keep launch URI explicit through config, manifest, or runtime descriptor. The profile does not create one.
- Preserve precedence: runtime descriptor > explicit/static Beacon endpoint profile > manifest connection fields.

## Validation

- Focused platform tests:
  - `SunshineEndpointProfileUsesDocumentedPortOffsets`
  - `StartIncludesSunshineEndpointProfileWhenExplicitEndpointsAreAbsent`
  - `ExplicitEndpointsOverrideSunshineEndpointProfile`
- Focused registration tests:
  - `ExternalProcessSunshineEndpointProfileUsesConfiguration`
  - `ExternalProcessSunshineEndpointProfileUsesEnvironmentOverrides`
- Full validation must include diff check, timeout-pattern audit, .NET format/build/test, Client Lab lint/test/Playwright, Android test/assemble, and display probe status before merging.
