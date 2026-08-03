#pragma once

#include <cstdint>

namespace beacon::worker::audio {

enum class ProductionAudioCapabilityBoundary {
  none,
  capture,
  encoder,
};

struct ProductionAudioCapabilities {
  bool available{};
  ProductionAudioCapabilityBoundary unavailable_boundary{
      ProductionAudioCapabilityBoundary::none};
  std::uint32_t unavailable_code{};
};

[[nodiscard]] ProductionAudioCapabilities
probe_windows_production_audio_capabilities();

} // namespace beacon::worker::audio
