#include "beacon/stream/media_datagram.h"

namespace beacon::stream {
namespace {

constexpr std::uint16_t known_flags =
    static_cast<std::uint16_t>(MediaDatagramFlags::idr) |
    static_cast<std::uint16_t>(MediaDatagramFlags::codec_configuration) |
    static_cast<std::uint16_t>(MediaDatagramFlags::end_of_access_unit);

void write_u16(std::span<std::byte, media_datagram_header_bytes> output,
               std::size_t offset,
               std::uint16_t value) noexcept {
  output[offset] = static_cast<std::byte>((value >> 8U) & 0xffU);
  output[offset + 1] = static_cast<std::byte>(value & 0xffU);
}

void write_u32(std::span<std::byte, media_datagram_header_bytes> output,
               std::size_t offset,
               std::uint32_t value) noexcept {
  for (std::size_t index = 0; index < 4; ++index) {
    output[offset + index] =
        static_cast<std::byte>((value >> ((3U - index) * 8U)) & 0xffU);
  }
}

void write_u64(std::span<std::byte, media_datagram_header_bytes> output,
               std::size_t offset,
               std::uint64_t value) noexcept {
  for (std::size_t index = 0; index < 8; ++index) {
    output[offset + index] =
        static_cast<std::byte>((value >> ((7U - index) * 8U)) & 0xffU);
  }
}

std::uint16_t read_u16(std::span<const std::byte> input, std::size_t offset) noexcept {
  return static_cast<std::uint16_t>((std::to_integer<std::uint16_t>(input[offset]) << 8U) |
                                    std::to_integer<std::uint16_t>(input[offset + 1]));
}

std::uint32_t read_u32(std::span<const std::byte> input, std::size_t offset) noexcept {
  std::uint32_t value = 0;
  for (std::size_t index = 0; index < 4; ++index) {
    value = (value << 8U) | std::to_integer<std::uint32_t>(input[offset + index]);
  }
  return value;
}

std::uint64_t read_u64(std::span<const std::byte> input, std::size_t offset) noexcept {
  std::uint64_t value = 0;
  for (std::size_t index = 0; index < 8; ++index) {
    value = (value << 8U) | std::to_integer<std::uint64_t>(input[offset + index]);
  }
  return value;
}

MediaDatagramError validate_header(const MediaDatagramHeader& header,
                                   std::uint16_t reserved,
                                   std::size_t actual_payload_bytes) noexcept {
  if (header.version != media_datagram_version) {
    return MediaDatagramError::unsupported_version;
  }
  if (header.media_kind != MediaKind::video && header.media_kind != MediaKind::audio) {
    return MediaDatagramError::invalid_media_kind;
  }
  if ((static_cast<std::uint16_t>(header.flags) & ~known_flags) != 0) {
    return MediaDatagramError::invalid_flags;
  }
  if (reserved != 0) {
    return MediaDatagramError::reserved_not_zero;
  }
  if (header.frame_bytes == 0 || header.frame_bytes > maximum_media_frame_bytes) {
    return MediaDatagramError::invalid_frame_size;
  }
  if (header.chunk_count == 0) {
    return MediaDatagramError::invalid_chunk_count;
  }
  if (header.chunk_index >= header.chunk_count) {
    return MediaDatagramError::invalid_chunk_index;
  }
  if (header.payload_bytes == 0 || header.payload_bytes != actual_payload_bytes) {
    return MediaDatagramError::payload_size_mismatch;
  }
  if (header.payload_offset > header.frame_bytes ||
      header.payload_bytes > header.frame_bytes - header.payload_offset) {
    return MediaDatagramError::payload_out_of_range;
  }
  return MediaDatagramError::none;
}

}  // namespace

bool serialize_media_datagram_header(
    const MediaDatagramHeader& header,
    std::span<std::byte, media_datagram_header_bytes> output) noexcept {
  const auto validation = validate_header(header, 0, header.payload_bytes);
  if (validation != MediaDatagramError::none) {
    return false;
  }

  write_u32(output, 0, media_datagram_magic);
  output[4] = static_cast<std::byte>(header.version);
  output[5] = static_cast<std::byte>(header.media_kind);
  write_u16(output, 6, static_cast<std::uint16_t>(header.flags));
  write_u64(output, 8, header.sequence);
  write_u64(output, 16, header.presentation_time_us);
  write_u32(output, 24, header.frame_bytes);
  write_u16(output, 28, header.chunk_index);
  write_u16(output, 30, header.chunk_count);
  write_u32(output, 32, header.payload_offset);
  write_u16(output, 36, header.payload_bytes);
  write_u16(output, 38, 0);
  return true;
}

ParsedMediaDatagram parse_media_datagram(std::span<const std::byte> datagram) noexcept {
  ParsedMediaDatagram result{};
  if (datagram.size() < media_datagram_header_bytes) {
    result.error = MediaDatagramError::header_too_small;
    return result;
  }
  if (read_u32(datagram, 0) != media_datagram_magic) {
    result.error = MediaDatagramError::invalid_magic;
    return result;
  }

  result.header.version = std::to_integer<std::uint8_t>(datagram[4]);
  result.header.media_kind = static_cast<MediaKind>(std::to_integer<std::uint8_t>(datagram[5]));
  result.header.flags = static_cast<MediaDatagramFlags>(read_u16(datagram, 6));
  result.header.sequence = read_u64(datagram, 8);
  result.header.presentation_time_us = read_u64(datagram, 16);
  result.header.frame_bytes = read_u32(datagram, 24);
  result.header.chunk_index = read_u16(datagram, 28);
  result.header.chunk_count = read_u16(datagram, 30);
  result.header.payload_offset = read_u32(datagram, 32);
  result.header.payload_bytes = read_u16(datagram, 36);

  const auto actual_payload_bytes = datagram.size() - media_datagram_header_bytes;
  result.error = validate_header(result.header, read_u16(datagram, 38), actual_payload_bytes);
  if (result.error == MediaDatagramError::none) {
    result.payload = datagram.subspan(media_datagram_header_bytes);
  }
  return result;
}

}  // namespace beacon::stream
