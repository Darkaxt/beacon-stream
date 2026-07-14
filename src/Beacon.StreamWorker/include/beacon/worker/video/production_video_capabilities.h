#pragma once

#include "beacon/worker/capture/wgc_display_capture.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"

#include <cstdint>

namespace beacon::worker::video {

enum class ProductionVideoCapabilityBoundary {
  none,
  capture,
  encoder,
};

struct ProductionVideoCapabilities {
  bool available{};
  ProductionVideoCapabilityBoundary unavailable_boundary{
      ProductionVideoCapabilityBoundary::none};
  std::uint32_t unavailable_code{};
};

[[nodiscard]] ProductionVideoCapabilities classify_production_video_capabilities(
    bool nvidia_adapter_available,
    NvencH264Failure nvenc_runtime_failure) noexcept;

[[nodiscard]] ProductionVideoCapabilities
probe_windows_production_video_capabilities() noexcept;

}  // namespace beacon::worker::video
