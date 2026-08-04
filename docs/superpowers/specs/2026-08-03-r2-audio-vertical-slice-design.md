# Beacon R2 Audio Vertical Slice

Status: approved implementation refinement, 2026-08-03

## Outcome

R2 audio is complete when one Beacon session captures the Windows default playback mix,
encodes it as Opus, carries it over the existing authenticated QUIC media transport, and plays it
through the Android client. Audio starts and stops with the same session generation as video.

## Fixed First-Release Mode

- Opus at 48 kHz, stereo, 20 ms frames, 96 kbit/s constant bitrate.
- Windows shared-mode WASAPI loopback capture of the default console render endpoint.
- One Opus packet per `MediaKind::audio` datagram.
- Native Opus decode in StreamCore and Android `AudioTrack` streaming playback.

The server owns this mode in `SessionPlan`. The public client grant, worker preparation command,
ticket authorization, and `StartSession` all carry the same selected audio facts. A mismatch fails
before media starts.

## Lifecycle

The worker starts audio only for an accepted session generation. Stop, connection loss, session
failure, and worker shutdown release capture and encoder ownership exactly once. Android playback
belongs to the same stream object and is stopped and released with that generation.

No cancellation sleeps or deadlines are introduced. Capture waits on the WASAPI event and an
explicit stop event. Existing transport/session events gate later work.

## Failure Contract

Audio capability is advertised truthfully. An unavailable capture or encoder path fails at the
audio boundary before application launch. Runtime failures identify capture, encoder, decoder, or
playback. R2 does not silently remove audio from a playable session.

## Verification Boundary

Focused tests cover plan/authorization equality, Opus packetization and decode, worker generation
ownership, and Android playback lifecycle. The first dynamic proof is one emulator-backed session
with decoded PCM/write evidence. Audible confirmation on the physical phone is the final R2 check.

## Deferred

Surround sound, alternate endpoint selection, process-specific capture, adaptive audio buffering,
additional audio codecs, lip-sync tuning, and audio UI are deferred until the first path works.

