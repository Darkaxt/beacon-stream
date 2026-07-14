# D3D11 Video Processor Validation

Date: 2026-07-14

Host GPU: NVIDIA GeForce RTX 4090 Laptop GPU

Physical capture target: `\\.\DISPLAY5`, 2560x1600

Command:

```powershell
& .\scripts\test-d3d11-video-processor.ps1 `
    -DisplayName '\\.\DISPLAY5' `
    -DisplayWidth 2560 `
    -DisplayHeight 1600
```

Observed output:

```text
BEACON_SOURCE_REVISION fe895eb6eb0f002e33a6b6ab0996410ae1d24396
[6/6] Linking CXX executable Beacon.StreamWorker.Tests\Debug\BeaconStreamWorkerWgcCaptureProbe.exe
bar=0 yuv=16,128,128
bar=1 yuv=235,128,128
bar=2 yuv=63,102,240
bar=3 yuv=173,42,26
bar=4 yuv=32,240,118
letterbox_yuv=16,128,128
BEACON_D3D11_VIDEO_PROCESSOR_OK adapter="NVIDIA GeForce RTX 4090 Laptop GPU" input=1280x720 output=640x400 tolerance=5
BEACON_WGC_CAPTURE_OK device=\\.\DISPLAY5 adapter=NVIDIA GeForce RTX 4090 Laptop GPU size=2560x1600 format=NV12 frame1=3353095535761089228 frame2=2879471302369438892 qpc1=7365817868355 qpc2=7365820665619
BEACON_D3D11_VIDEO_VALIDATION_OK
```

The first probe verifies the exact BT.709 limited-range samples, 16:9-to-16:10 scaling,
limited-black letterboxing, and NV12 output dimensions. The second probe proves two changing
real WGC frames on the capture device with increasing QPC timestamps. The validation script
builds both probes from the named Git revision before running them. It does not create or
change a display, query or control Apollo, or use ADB/emulator state. The refactor commit
will receive a clean-tree recapture before this evidence is finalized.
