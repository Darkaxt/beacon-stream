# R2 Audio Vertical Slice Plan

1. Add one immutable Opus stereo mode to the server plan, worker command, ticket grant, and client
   start handshake.
2. Add event-driven Windows loopback capture and Opus encode to the existing worker generation and
   media transport.
3. Route audio datagrams through StreamCore, decode Opus, and write PCM through one Android
   `AudioTrack` owner.
4. Prove the focused audio transaction in native, managed, and emulator tests. Run the guarded
   integrated transaction only when the Windows input desktop is available, and always verify the
   physical display afterward.

Broad repository matrices, tuning, and refactoring remain R3/final-QA work.

