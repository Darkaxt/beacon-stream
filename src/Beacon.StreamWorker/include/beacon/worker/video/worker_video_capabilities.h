#pragma once

#include <cstdint>

namespace beacon::worker::video {

enum class ProductionVideoCapabilityBoundary {
  none,
  capture,
  encoder,
};

struct ProductionVideoCapabilities {
  bool available{};
  bool hevc_main10_hdr10_available{};
  ProductionVideoCapabilityBoundary unavailable_boundary{
      ProductionVideoCapabilityBoundary::none};
  std::uint32_t unavailable_code{};
};

} // namespace beacon::worker::video
