#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

namespace beacon::stream {

inline constexpr std::size_t media_datagram_header_bytes = 40;
inline constexpr std::uint32_t media_datagram_magic = 0x42535452U;
inline constexpr std::uint8_t media_datagram_version = 1;
inline constexpr std::uint32_t maximum_media_frame_bytes = 16U * 1024U * 1024U;

enum class MediaKind : std::uint8_t {
  video = 1,
  audio = 2,
};

enum class MediaDatagramFlags : std::uint16_t {
  none = 0,
  idr = 1U << 0U,
  codec_configuration = 1U << 1U,
  end_of_access_unit = 1U << 2U,
};

[[nodiscard]] constexpr MediaDatagramFlags operator|(MediaDatagramFlags left,
                                                      MediaDatagramFlags right) noexcept {
  return static_cast<MediaDatagramFlags>(static_cast<std::uint16_t>(left) |
                                         static_cast<std::uint16_t>(right));
}

struct MediaDatagramHeader {
  std::uint8_t version{media_datagram_version};
  MediaKind media_kind{MediaKind::video};
  MediaDatagramFlags flags{MediaDatagramFlags::none};
  std::uint64_t sequence{};
  std::uint64_t presentation_time_us{};
  std::uint32_t frame_bytes{};
  std::uint16_t chunk_index{};
  std::uint16_t chunk_count{};
  std::uint32_t payload_offset{};
  std::uint16_t payload_bytes{};
};

enum class MediaDatagramError {
  none,
  header_too_small,
  invalid_magic,
  unsupported_version,
  invalid_media_kind,
  invalid_flags,
  reserved_not_zero,
  invalid_frame_size,
  invalid_chunk_count,
  invalid_chunk_index,
  payload_size_mismatch,
  payload_out_of_range,
};

struct ParsedMediaDatagram {
  MediaDatagramHeader header{};
  std::span<const std::byte> payload{};
  MediaDatagramError error{MediaDatagramError::none};
};

[[nodiscard]] bool serialize_media_datagram_header(
    const MediaDatagramHeader& header,
    std::span<std::byte, media_datagram_header_bytes> output) noexcept;

[[nodiscard]] ParsedMediaDatagram parse_media_datagram(
    std::span<const std::byte> datagram) noexcept;

}  // namespace beacon::stream
