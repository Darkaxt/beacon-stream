# Beacon Production Video Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compose Beacon's validated native capture, conversion, encoder, recovery, and QUIC primitives into the one production StreamWorker moving-video path.

**Architecture:** `WorkerHost` validates and stores a plain immutable video plan while `WorkerVideoPipeline` owns reconnectable generation orchestration. Each authenticated QUIC generation creates one `ProductionVideoGeneration` containing WGC, D3D11 processing, NVENC, and `VideoMediaSession`; disconnect destroys only that generation and preserves the prepared plan.

**Tech Stack:** C++20, CMake/CTest, Windows Graphics Capture, D3D11, NVENC, MsQuic, Protobuf, PowerShell validation.

---

### Task 1: Bind Every Stream Ticket To The Prepared Video Mode

**Files:**
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_ticket_store.h`
- Modify: `src/Beacon.StreamWorker/src/quic_ticket_store.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_session_protocol.h`
- Modify: `src/Beacon.StreamWorker/src/quic_session_protocol.cpp`
- Modify: `tests/Beacon.StreamWorker.Tests/quic_session_tests.cpp`

- [x] **Step 1: Write failing ticket and protocol tests**

Add a selected H.264 SDR mode to normal ticket grants. Prove authorization rejects tickets
with neither or both video and benchmark operations, authentication retains the selected mode,
an exact `StartSession` is accepted, and any width/FPS/codec/range mismatch closes the
connection without publishing `AcceptedStartSession`.

- [x] **Step 2: Run the focused test and verify RED**

Run:

```powershell
cmake --build native/build --config Debug --target BeaconStreamWorkerQuicSessionTests
ctest --test-dir native/build -C Debug -R BeaconStreamWorker.QuicSession --output-on-failure
```

Expected: failure because authorized tickets do not carry a selected video mode and
`QuicSessionProtocol` accepts any syntactically valid `StartSession`.

- [x] **Step 3: Implement exact operation binding**

Add `optional<SelectedVideoMode>` to `AuthorizedQuicTicket` and
`QuicTicketConsumeOutcome`, require exactly one video/benchmark operation, retain the consumed
video mode in `QuicSessionProtocol`, compare every field exactly, and clear it on reset.

- [x] **Step 4: Re-run the focused test and verify GREEN**

Expected: `BeaconStreamWorker.QuicSession` passes.

### Task 2: Define And Test Generation Ownership

**Files:**
- Create: `src/Beacon.StreamWorker/include/beacon/worker/video/worker_video_pipeline.h`
- Create: `src/Beacon.StreamWorker/src/video/worker_video_pipeline.cpp`
- Create: `tests/Beacon.StreamWorker.Tests/worker_video_pipeline_tests.cpp`
- Modify: `tests/Beacon.StreamWorker.Tests/CMakeLists.txt`

- [x] **Step 1: Write failing controller tests**

Define tests around a fake `IVideoPipelineGenerationFactory`. Cover plain-plan validation,
matching authenticated start, stale event rejection, matching stop, transport disconnect,
explicit stop, reconnect with a new generation object, feedback forwarding, explicit IDR, and
exactly-once teardown.

- [x] **Step 2: Build the new target and verify RED**

Run the new `BeaconStreamWorker.VideoPipeline` CTest and expect compile failure because the
controller API does not exist.

- [x] **Step 3: Implement the minimal controller**

Create `WorkerVideoPlan`, `IVideoPipelineGeneration`,
`IVideoPipelineGenerationFactory`, and `IWorkerVideoPipeline`. Implement a mutex-protected
`WorkerVideoPipeline` that invokes generation objects outside its lock and preserves only the
immutable prepared plan across disconnect.

- [x] **Step 4: Re-run the controller test and verify GREEN**

Expected: all generation ownership tests pass without sleeps or lifecycle timeouts.

### Task 3: Compose WGC, D3D11, NVENC, And VideoMediaSession

**Files:**
- Create: `src/Beacon.StreamWorker/include/beacon/worker/video/production_video_generation.h`
- Create: `src/Beacon.StreamWorker/src/video/production_video_generation.cpp`
- Create: `tests/Beacon.StreamWorker.Tests/production_video_generation_tests.cpp`
- Modify: `src/Beacon.StreamWorker/CMakeLists.txt`
- Modify: `tests/Beacon.StreamWorker.Tests/CMakeLists.txt`

- [x] **Step 1: Write a failing end-to-end native composition test**

Inject fake WGC, D3D11, and NVENC platform APIs into a real
`ProductionVideoGeneration`. Emit a changing captured frame and assert that the real processor,
encoder, packetizer, and `VideoMediaSession` produce generation-bound datagrams with an initial
IDR, SPS/PPS, and microsecond presentation timestamp. Add feedback, bitrate, reconnect, failure,
and release-once cases.

- [x] **Step 2: Build the new target and verify RED**

Run the new `BeaconStreamWorker.ProductionVideoGeneration` CTest and expect compile failure
because the production composition class does not exist.

- [x] **Step 3: Implement the production generation and factory**

Construct `WgcDisplayCapture`, `D3d11VideoProcessor`, `NvencH264Encoder`, and
`VideoMediaSession` from the immutable plan. Process frames only on the WGC consumer thread,
apply pending controls before encode, divide WGC 100-nanosecond timestamps by ten, and stop WGC
before releasing dependent GPU objects.

- [x] **Step 4: Re-run the production composition test and verify GREEN**

Expected: real composition with fake native boundaries passes deterministically.

### Task 4: Integrate The Pipeline Into WorkerHost And The Executable

**Files:**
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/worker_host.h`
- Modify: `src/Beacon.StreamWorker/src/worker_host.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/quic_listener.h`
- Modify: `src/Beacon.StreamWorker/src/quic_listener.cpp`
- Modify: `src/Beacon.StreamWorker/include/beacon/worker/worker_events.h`
- Modify: `src/Beacon.StreamWorker/src/worker_events.cpp`
- Modify: `src/Beacon.StreamWorker/src/main.cpp`
- Modify: `tests/Beacon.StreamWorker.Tests/worker_host_tests.cpp`

- [x] **Step 1: Add failing WorkerHost lifecycle tests**

Inject a recording pipeline and prove prepare receives the complete immutable plan, bitrate
bounds are ordered, explicit IDR reaches the active pipeline, stop/shutdown stop the pipeline
before transport closure, benchmark preparation clears video state, and authorization binds the
prepared video mode.

- [x] **Step 2: Run WorkerHost tests and verify RED**

Expected: failure because `WorkerHost` has no production-video dependency and does not retain
the prepared plan.

- [x] **Step 3: Wire production ownership**

Inject `IWorkerVideoPipeline` into `WorkerHost`, connect `QuicListener` media events to the
pipeline in `main.cpp`, add event-driven active-connection disconnect support, publish typed
pipeline failures through the existing outbound queue, and stop the pipeline before transport
shutdown on every IPC/process exit path.

- [x] **Step 4: Run WorkerHost and full native tests**

Expected: all CTests pass and no test uses wall-clock cancellation timeouts.

### Task 5: Validate, Sync, Refactor, Validate, Sync

- [x] **Step 1: Run static and dynamic implementation validation**

Run formatting, architecture guards, the full native CTest suite, the Worker IPC/QUIC process
probe with real access-unit evidence, the complete .NET suite, Android JVM/build checks, and
Client Lab tests. Scan tracked first-party paths for prohibited upstream runtime references and
parallel media routes. Do not use ADB or touch Apollo.

- [x] **Step 2: Record evidence, commit, and push the implementation sync**

Update `docs/validation` and the Gate 5 plan with exact commands/results, commit the coherent
implementation, and push `codex/beacon-production-benchmarks`.

- [x] **Step 3: Audit and refactor ownership**

Review callback lifetime, stale generation handling, stop ordering, native failure diagnostics,
plan binding, dead synthetic production paths, and source provenance. Add a failing regression
before every behavioral fix and remove only code proven obsolete.

- [x] **Step 4: Repeat the entire validation matrix**

Expected: the same static and dynamic evidence passes after refactoring.

- [ ] **Step 5: Commit and push the refactor sync**

Leave emulator acceptance open until the shared ADB runtime is available; do not substitute
Apollo or another client path.
