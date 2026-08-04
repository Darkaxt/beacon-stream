#include "beacon/worker/audio/production_audio_capabilities.h"

#include "beacon/worker/audio/opus_audio_encoder.h"
#include "beacon/worker/audio/wasapi_loopback_capture.h"

namespace beacon::worker::audio {

ProductionAudioCapabilities probe_windows_production_audio_capabilities() {
  OpusAudioEncoder encoder;
  if (!encoder.ready()) {
    return {
        .available = false,
        .unavailable_boundary = ProductionAudioCapabilityBoundary::encoder,
        .unavailable_code = static_cast<std::uint32_t>(encoder.opus_error()),
    };
  }

  WasapiLoopbackCapture capture;
  if (!capture.start([](CapturedAudioFrame) {})) {
    const auto failure = capture.failure();
    return {
        .available = false,
        .unavailable_boundary = ProductionAudioCapabilityBoundary::capture,
        .unavailable_code = failure.native_code != 0
                                ? failure.native_code
                                : static_cast<std::uint32_t>(failure.stage),
    };
  }
  capture.stop();
  return {.available = true};
}

} // namespace beacon::worker::audio
