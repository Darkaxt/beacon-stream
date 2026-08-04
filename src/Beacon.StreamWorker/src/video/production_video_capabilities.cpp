#include "beacon/worker/video/production_video_capabilities.h"

#include <algorithm>

namespace beacon::worker::video {
namespace {

constexpr std::uint32_t nvidia_vendor_id{0x10de};

}  // namespace

ProductionVideoCapabilities classify_production_video_capabilities(
    bool nvidia_adapter_available,
    NvencH264Failure nvenc_runtime_failure) noexcept {
  if (!nvidia_adapter_available) {
    return {
        .available = false,
        .unavailable_boundary = ProductionVideoCapabilityBoundary::capture,
        .unavailable_code = static_cast<std::uint32_t>(
            capture::WgcCaptureFailure::nvidia_adapter_missing),
    };
  }
  if (nvenc_runtime_failure != NvencH264Failure::none) {
    return {
        .available = false,
        .unavailable_boundary = ProductionVideoCapabilityBoundary::encoder,
        .unavailable_code =
            static_cast<std::uint32_t>(nvenc_runtime_failure),
    };
  }
  return {.available = true};
}

ProductionVideoCapabilities
probe_windows_production_video_capabilities() noexcept {
  try {
    const auto platform = capture::create_windows_wgc_capture_platform();
    const auto adapters = platform->graphics_adapters();
    const bool nvidia_adapter_available = std::ranges::any_of(
        adapters, [](const capture::WgcAdapterSnapshot& adapter) {
          return adapter.vendor_id == nvidia_vendor_id && !adapter.software;
        });
    auto capabilities = classify_production_video_capabilities(
        nvidia_adapter_available,
        nvidia_adapter_available ? probe_windows_nvenc_h264_capabilities()
                                 : NvencH264Failure::none);
    capabilities.hevc_main10_hdr10_available =
        capabilities.available &&
        probe_windows_d3d11_hdr10_video_conversion() ==
            D3d11VideoProcessorFailure::none &&
        probe_windows_nvenc_hevc_main10_capabilities() ==
            NvencH264Failure::none;
    return capabilities;
  } catch (...) {
    return {
        .available = false,
        .unavailable_boundary = ProductionVideoCapabilityBoundary::capture,
        .unavailable_code = static_cast<std::uint32_t>(
            capture::WgcCaptureFailure::capture_start_failed),
    };
  }
}

}  // namespace beacon::worker::video
