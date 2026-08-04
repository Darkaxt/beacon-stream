#include "beacon/stream/hdr_static_metadata.h"

#include <array>

namespace beacon::stream {
namespace {

std::uint16_t little_endian_u16(std::string_view bytes,
                                std::size_t offset) noexcept {
  return static_cast<std::uint16_t>(
      static_cast<unsigned char>(bytes[offset]) |
      (static_cast<std::uint16_t>(
           static_cast<unsigned char>(bytes[offset + 1U]))
       << 8U));
}

HdrChromaticity chromaticity(std::string_view bytes,
                             std::size_t offset) noexcept {
  return {.x = little_endian_u16(bytes, offset),
          .y = little_endian_u16(bytes, offset + 2U)};
}

bool valid_chromaticity(HdrChromaticity value) noexcept {
  constexpr std::uint16_t maximum_coordinate{50'000};
  return value.x <= maximum_coordinate && value.y <= maximum_coordinate;
}

}  // namespace

std::optional<HdrStaticMetadata> parse_cta861_3_hdr_static_info(
    std::string_view bytes) noexcept {
  if (bytes.size() != 25 ||
      static_cast<unsigned char>(bytes.front()) != 0) {
    return std::nullopt;
  }
  const HdrStaticMetadata metadata{
      .red = chromaticity(bytes, 1),
      .green = chromaticity(bytes, 5),
      .blue = chromaticity(bytes, 9),
      .white = chromaticity(bytes, 13),
      .maximum_mastering_luminance = little_endian_u16(bytes, 17),
      .minimum_mastering_luminance = little_endian_u16(bytes, 19),
      .maximum_content_light_level = little_endian_u16(bytes, 21),
      .maximum_frame_average_light_level = little_endian_u16(bytes, 23),
  };
  const std::array coordinates{metadata.red, metadata.green, metadata.blue,
                               metadata.white};
  for (const auto value : coordinates) {
    if (!valid_chromaticity(value)) {
      return std::nullopt;
    }
  }
  if (metadata.maximum_mastering_luminance == 0 ||
      static_cast<std::uint32_t>(metadata.minimum_mastering_luminance) >
          static_cast<std::uint32_t>(
              metadata.maximum_mastering_luminance) * 10'000U ||
      (metadata.maximum_content_light_level != 0 &&
       metadata.maximum_frame_average_light_level >
           metadata.maximum_content_light_level)) {
    return std::nullopt;
  }
  return metadata;
}

}  // namespace beacon::stream
