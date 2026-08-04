# HDR10 Pipeline Validation

Date: 2026-08-04

## Implemented Path

Beacon has one end-to-end production HDR contract:

1. Windows Graphics Capture retains HDR content in FP16 scRGB.
2. D3D11 converts FP16 scRGB directly to P010 BT.2020/PQ without an 8-bit intermediate.
3. NVENC produces HEVC Main10 with limited range, BT.2020 primaries, SMPTE ST 2084 transfer,
   BT.2020 non-constant-luminance matrix coefficients, and mastering-display/content-light SEI.
4. Worker IPC and the authenticated stream grant carry the exact codec, profile, bit depth,
   colorimetry, range, and static metadata tuple.
5. Android StreamCore maps that tuple to one exact MediaCodec decoder and an HDR presentation Surface.
6. Decoder-output mismatches fail closed. Physical HDR is reported only after decoded-frame and
   display-presentation postconditions succeed.

H.264 High 8-bit BT.709 limited-range SDR remains the independent fallback path.

## Retained Evidence

- Managed solution validation: format clean and 922 tests passed.
- Windows native validation: 36 of 36 tests passed.
- NVIDIA hardware probe on `NVIDIA GeForce RTX 4090 Laptop GPU`: four HEVC Main10 frames encoded and
  decoded, IDR behavior and bitrate reconfiguration passed, and independent parsing found Main10,
  `yuv420p10le`, limited range, BT.2020, SMPTE ST 2084, and HDR10 SEI.
- H.264 hardware regression probe: four High-profile BT.709 limited-range frames passed.
- Reference vector: 30-frame HEVC Main10 HDR10 access-unit stream with SHA-256
  `5ec0b193c717c4b96f3350a0e45d83d847a9ba27c1b9cd24b6f958f9817508b1` passed structural and decode
  validation.
- Android unit validation: 206 tests passed. Native StreamCore and lifecycle test binaries passed.
- Android emulator instrumentation: eight executions across `emulator-5554` and `emulator-5560`;
  six passed and two were correctly skipped because neither emulator exposes an exact HEVC Main10
  HDR10 decoder.
- The hosted H.264 SDR regression transaction authenticated through the strict full video tuple,
  sent 30 access units, received 30 rendered-frame acknowledgements, and observed 30 pixel variants.
- Independent Android build with an isolated Gradle home and Temurin 21 completed all 85 tasks.

## Certification Boundary

The Worker hardware path and the Android contract are validated. Physical Android HDR presentation is
not certified because no real HDR-capable Android device was connected. Emulator results cannot prove
panel HDR state, and Beacon does not infer it from a requested color mode or decoder configuration.

No display-topology mutation was needed for this validation pass. Final read-only status showed only
physical `DISPLAY5` active and primary at `2560x1600@240`, mirror mode disabled, zero HostAgent leases,
and no active lease heartbeat.
