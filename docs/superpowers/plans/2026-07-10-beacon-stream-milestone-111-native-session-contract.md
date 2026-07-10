# Beacon Stream Milestone 111: Native Session Contract

## Goal

Define and validate the complete server-provisioned Moonlight native session contract required to call `LiStartConnection`, while keeping ephemeral key material private to the owning client.

## Contract Boundary

The backend-provided native session contains:

- Host address, server app version, optional GFE version, RTSP session URL, and server codec support mask.
- Width, height, FPS, bitrate, packet size, streaming mode, audio configuration, selected video format, client refresh rate, color space/range, and encryption flags.
- The 16-byte remote-input AES key and IV generated for the host launch request.

Beacon Server remains authoritative for all policy values. Android validates and converts the wire representation into exact `moonlight-common-c` inputs; it does not choose a different codec, resolution, FPS, bitrate, HDR mode, or transport.

## Secret Handling

- Native session key material must not be added to `StreamingSessionState` or `StreamingConnectionDescriptor`.
- It must not appear in `/admin/snapshot`, Cockpit state, diagnostics, logs, process arguments, or environment variables.
- The streaming backend keeps the descriptor in a private per-session store.
- Only the owning client's launch and stream-status responses may include it.
- Stop removes the private descriptor.

## Test-First Sequence

1. Add Core tests for strict native-session validation, including 16-byte Base64 key/IV checks and enum allowlists.
2. Add Windows backend tests proving a runtime wrapper descriptor is stored privately and returned through an owning-client-only accessor.
3. Add Server API tests proving launch/stream responses include the native session while `/admin/snapshot` does not expose either secret.
4. Add Android JVM tests for extraction and exact mapping into a typed native session plan.
5. Add Android tests for missing fields, malformed Base64, wrong key lengths, unsupported enum values, and protocol mismatch.

## Scope

- Add typed C# and Java native-session descriptor models.
- Extend the external runtime descriptor JSON contract with an optional native session.
- Add a default-safe backend accessor so existing streaming backends require no secret implementation.
- Carry the descriptor into client launch and active-stream responses without changing public session snapshots.
- Parse and validate the contract in the APK, but do not call `LiStartConnection` yet.
- Document the exact JSON contract and redaction invariant.

## Validation And Sync

1. Focused Core, Windows backend, Server API, and Android JVM tests.
2. Full `.NET`, Android, Client Lab, and Playwright validation.
3. Emulator install/launch plus a recorded fake response parse check.
4. Commit, push, PR, CI, and merge.
5. Review for duplication or secret-lifetime simplification.
6. Refactor if justified, re-run static/dynamic checks, and sync again.

## Exit Evidence

- Every current `SERVER_INFORMATION` and `STREAM_CONFIGURATION` field has one validated wire representation.
- Key and IV material are delivered only to the owning client API surface.
- Admin/public session serialization contains no key or IV.
- Android can parse a valid contract and reject malformed or incomplete contracts deterministically.
- No code claims the native stream has started before the following host-provisioning milestone supplies a real descriptor.
