#include "beacon/stream/media_datagram.h"

#include <array>
#include <cassert>
#include <cstddef>
#include <cstdint>
#include <span>

namespace {

using beacon::stream::MediaDatagramError;
using beacon::stream::MediaDatagramFlags;
using beacon::stream::MediaDatagramHeader;
using beacon::stream::MediaKind;

constexpr std::array<std::byte, 43> expected_datagram{
    std::byte{0x42}, std::byte{0x53}, std::byte{0x54}, std::byte{0x52},
    std::byte{0x01}, std::byte{0x01}, std::byte{0x00}, std::byte{0x05},
    std::byte{0x01}, std::byte{0x02}, std::byte{0x03}, std::byte{0x04},
    std::byte{0x05}, std::byte{0x06}, std::byte{0x07}, std::byte{0x08},
    std::byte{0x11}, std::byte{0x12}, std::byte{0x13}, std::byte{0x14},
    std::byte{0x15}, std::byte{0x16}, std::byte{0x17}, std::byte{0x18},
    std::byte{0x00}, std::byte{0x00}, std::byte{0x10}, std::byte{0x00},
    std::byte{0x00}, std::byte{0x01}, std::byte{0x00}, std::byte{0x03},
    std::byte{0x00}, std::byte{0x00}, std::byte{0x04}, std::byte{0x00},
    std::byte{0x00}, std::byte{0x03}, std::byte{0x00}, std::byte{0x00},
    std::byte{0xaa}, std::byte{0xbb}, std::byte{0xcc},
};

constexpr MediaDatagramHeader valid_header{
    .version = 1,
    .media_kind = MediaKind::video,
    .flags = MediaDatagramFlags::idr | MediaDatagramFlags::end_of_access_unit,
    .sequence = 0x0102030405060708ULL,
    .presentation_time_us = 0x1112131415161718ULL,
    .frame_bytes = 4096,
    .chunk_index = 1,
    .chunk_count = 3,
    .payload_offset = 1024,
    .payload_bytes = 3,
};

void serialization_uses_the_fixed_network_order_vector() {
  std::array<std::byte, 40> output{};
  assert(beacon::stream::serialize_media_datagram_header(valid_header, output));
  assert(std::equal(output.begin(), output.end(), expected_datagram.begin()));
}

void parsing_returns_header_and_zero_copy_payload() {
  const auto result = beacon::stream::parse_media_datagram(expected_datagram);
  assert(result.error == MediaDatagramError::none);
  assert(result.header.sequence == valid_header.sequence);
  assert(result.header.presentation_time_us == valid_header.presentation_time_us);
  assert(result.header.frame_bytes == valid_header.frame_bytes);
  assert(result.payload.size() == 3);
  assert(result.payload.data() == expected_datagram.data() + 40);
}

void malformed_datagrams_are_rejected() {
  auto malformed = expected_datagram;
  malformed[0] = std::byte{0};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_magic);

  malformed = expected_datagram;
  malformed[4] = std::byte{2};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::unsupported_version);

  malformed = expected_datagram;
  malformed[7] = std::byte{0x80};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_flags);

  malformed = expected_datagram;
  malformed[39] = std::byte{1};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::reserved_not_zero);

  malformed = expected_datagram;
  malformed[24] = std::byte{0x01};
  malformed[27] = std::byte{0x01};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_frame_size);

  malformed = expected_datagram;
  malformed[30] = std::byte{0};
  malformed[31] = std::byte{0};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_chunk_count);

  malformed = expected_datagram;
  malformed[30] = std::byte{0};
  malformed[31] = std::byte{1};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_chunk_index);

  malformed = expected_datagram;
  malformed[34] = std::byte{0x10};
  malformed[35] = std::byte{0x00};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::payload_out_of_range);

  malformed = expected_datagram;
  malformed[37] = std::byte{2};
  assert(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::payload_size_mismatch);

  assert(beacon::stream::parse_media_datagram(
             std::span<const std::byte>{expected_datagram.data(), expected_datagram.size() - 1})
             .error == MediaDatagramError::payload_size_mismatch);
}

}  // namespace

int main() {
  static_assert(beacon::stream::media_datagram_header_bytes == 40);
  serialization_uses_the_fixed_network_order_vector();
  parsing_returns_header_and_zero_copy_payload();
  malformed_datagrams_are_rejected();
  return 0;
}
