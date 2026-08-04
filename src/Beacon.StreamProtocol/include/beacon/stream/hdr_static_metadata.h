#pragma once

#include <cstdint>
#include <optional>
#include <string_view>

namespace beacon::stream {

struct HdrChromaticity {
  std::uint16_t x{};
  std::uint16_t y{};

  bool operator==(const HdrChromaticity&) const = default;
};

struct HdrStaticMetadata {
  HdrChromaticity red;
  HdrChromaticity green;
  HdrChromaticity blue;
  HdrChromaticity white;
  std::uint16_t maximum_mastering_luminance{};
  std::uint16_t minimum_mastering_luminance{};
  std::uint16_t maximum_content_light_level{};
  std::uint16_t maximum_frame_average_light_level{};

  bool operator==(const HdrStaticMetadata&) const = default;
};

[[nodiscard]] std::optional<HdrStaticMetadata>
parse_cta861_3_hdr_static_info(std::string_view bytes) noexcept;

}  // namespace beacon::stream
