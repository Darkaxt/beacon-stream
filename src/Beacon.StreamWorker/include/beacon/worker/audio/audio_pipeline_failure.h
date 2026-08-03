#pragma once

#include <cstdint>
#include <functional>
#include <string>

namespace beacon::worker::audio {

enum class AudioPipelineFailureBoundary {
  capture,
  encoder,
  media_session,
  transport,
};

struct AudioPipelineFailureEvent {
  std::string session_id;
  std::uint64_t session_generation{};
  AudioPipelineFailureBoundary boundary{AudioPipelineFailureBoundary::capture};
  std::string failure_stage;
  std::uint32_t native_code{};
};

using AudioPipelineFailureSink = std::function<void(AudioPipelineFailureEvent)>;

} // namespace beacon::worker::audio
