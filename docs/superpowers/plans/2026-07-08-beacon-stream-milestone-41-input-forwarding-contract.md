# Milestone 41 - Input Forwarding Contract

## Objective

Add a phone-free input forwarding contract between the client control plane and server-owned active stream sessions, without implementing native Windows input injection or copying Sunshine input code.

## Requirements Covered

- `REQ-SYNC-001`: Follow implement, validate, sync, refactor, validate, sync.
- `REQ-CTRL-009`: The client consumes server session state instead of choosing display topology locally.
- `REQ-REC-008`: Keep stream/session failures visible when input cannot be accepted.
- `REQ-TEST-001`: Keep validation possible without a phone.
- `REQ-TEST-003`: Extend simulator flows with input forwarding.

## Implementation Plan

1. Add red tests for `/clients/{clientId}/input`, FakeEndpoint, Client Lab, and Android input payload support.
2. Add `Beacon.Core.Input` with a typed input batch, event, result, and replaceable sink interface.
3. Add a client input endpoint that resolves the active session/display context before forwarding input.
4. Add deterministic pointer input samples to FakeEndpoint, Client Lab, and Android.
5. Validate focused tests, full static/dynamic checks, and sync through GitHub.

## Non-Goals

- No Windows `SendInput` or ViGEm injection.
- No Sunshine protocol input channel.
- No phone-specific gesture mapping.
- No timeout-based input queueing or cancellation.
