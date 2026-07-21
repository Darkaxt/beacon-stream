#pragma once

#include <cstdint>
#include <functional>
#include <string>

namespace beacon::worker::video {

enum class VideoPipelineFailureBoundary {
  capture,
  video_processor,
  encoder,
  media_session,
  transport,
};

struct VideoPipelineFailureEvent {
  std::string session_id;
  std::uint64_t session_generation{};
  VideoPipelineFailureBoundary boundary{VideoPipelineFailureBoundary::capture};
  std::string failure_stage;
  std::uint32_t native_code{};
};

using VideoPipelineFailureSink =
    std::function<void(VideoPipelineFailureEvent)>;

} // namespace beacon::worker::video
