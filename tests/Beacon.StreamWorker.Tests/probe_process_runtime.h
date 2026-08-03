#pragma once

namespace beacon::worker::tests {

enum class ProbePrepareDisposition {
  succeeded,
  unsupported_video_hardware,
  worker_rejected,
  exchange_failed,
};

struct ProbePrepareFacts {
  bool video_mode{};
  bool advertised_video_available{};
  bool advertised_audio_available{};
  bool exchange_succeeded{};
  bool has_completion{};
  bool completion_succeeded{};
  bool capability_unavailable{};
};

ProbePrepareDisposition
classify_probe_prepare(ProbePrepareFacts facts) noexcept;

void configure_noninteractive_probe_process() noexcept;

} // namespace beacon::worker::tests
