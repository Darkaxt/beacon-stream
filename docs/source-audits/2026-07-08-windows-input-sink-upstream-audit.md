# Windows Input Sink Upstream Audit

Milestone 43 adds Beacon's first real Windows input sink. This audit records the upstream source comparison used to shape that implementation. No upstream source was copied.

## Sources Inspected

| Project | Commit | Relevant files |
| --- | --- | --- |
| Sunshine | `0a47d3d0f7994de2a912f62dfe33ce2a20c2cd0d` | [`src/input.cpp`](https://github.com/LizardByte/Sunshine/blob/0a47d3d0f7994de2a912f62dfe33ce2a20c2cd0d/src/input.cpp), [`src/video.cpp`](https://github.com/LizardByte/Sunshine/blob/0a47d3d0f7994de2a912f62dfe33ce2a20c2cd0d/src/video.cpp) |
| Moonlight Android | `f10085f552b367cf7203007693d91c322a0a2936` | [`app/src/main/res/values/strings.xml`](https://github.com/moonlight-stream/moonlight-android/blob/f10085f552b367cf7203007693d91c322a0a2936/app/src/main/res/values/strings.xml) |
| Apollo | `adc5c5a0bd80831ce495434bb16aee2cd4175fb8` | [`src/display_device.cpp`](https://github.com/ClassicOldSong/Apollo/blob/adc5c5a0bd80831ce495434bb16aee2cd4175fb8/src/display_device.cpp) |
| Vibeshine | `31d7fcde7ab124478a2a250b2361ebeaf5e18813` | [`src/platform/windows/virtual_display_sunshine.cpp`](https://github.com/Nonary/vibeshine/blob/31d7fcde7ab124478a2a250b2361ebeaf5e18813/src/platform/windows/virtual_display_sunshine.cpp) |

## Findings

Sunshine maps client absolute input through a stream/display touch port before passing it to the platform input backend. It also raises that touch-port geometry from the active display during video setup. Beacon should therefore avoid a global, display-agnostic input path.

Moonlight Android keeps touch/trackpad/absolute-mouse behavior as client-side settings. Beacon's server sink should consume explicit client input events, but it should not absorb local touch-layout or UI preferences into server-global settings.

Apollo applies display preparation, HDR choice, resolution, refresh, and mode remapping from session/configuration state before the stream path proceeds. Beacon's input sink should only operate after a session plan and active display have already been resolved by the server.

Vibeshine's virtual-display code has extensive driver lease, identity, readiness, and reuse handling. Beacon should keep this milestone narrow: use the already active per-client display id from the session plan, fail when that display is absent, and leave driver lease policy in the display backend.

## Beacon Decisions

- Register `WindowsClientInputSink` only in Windows host mode. Fake host mode keeps `NoOpClientInputSink`.
- Resolve `ClientInputBatch.DisplayId` against the active Windows topology before any input is sent.
- Translate normalized pointer coordinates into desktop pixel coordinates for the resolved display.
- Support pointer `move`, `down`, `up`, and `tap` first. Keyboard, controller, native touch, and multitouch remain future work.
- Fail unsupported input types, actions, button masks, missing display ids, and invalid coordinates explicitly.
- Keep the Win32 `SendInput` call behind `IWindowsInputApi` so tests can validate policy without moving the real cursor.

## Not Implemented

- No GameStream input protocol parsing.
- No keyboard scancode mapping.
- No ViGEm/gamepad support.
- No native Windows touch injection.
- No client-side touch layout changes.
- No new timeouts, watchdogs, or polling loops.
