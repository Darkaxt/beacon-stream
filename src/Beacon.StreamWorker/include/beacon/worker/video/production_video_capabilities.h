#pragma once

#include "beacon/worker/capture/wgc_display_capture.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"
#include "beacon/worker/video/worker_video_capabilities.h"

namespace beacon::worker::video {

[[nodiscard]] ProductionVideoCapabilities classify_production_video_capabilities(
    bool nvidia_adapter_available,
    NvencH264Failure nvenc_runtime_failure) noexcept;

[[nodiscard]] ProductionVideoCapabilities
probe_windows_production_video_capabilities() noexcept;

}  // namespace beacon::worker::video
