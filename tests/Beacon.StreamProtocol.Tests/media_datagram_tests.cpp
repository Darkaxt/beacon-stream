#include "beacon/stream/media_datagram.h"

#include <array>
#include "test_failure.h"
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
  BEACON_TEST_REQUIRE(beacon::stream::serialize_media_datagram_header(valid_header, output));
  BEACON_TEST_REQUIRE(std::equal(output.begin(), output.end(), expected_datagram.begin()));
}

void parsing_returns_header_and_zero_copy_payload() {
  const auto result = beacon::stream::parse_media_datagram(expected_datagram);
  BEACON_TEST_REQUIRE(result.error == MediaDatagramError::none);
  BEACON_TEST_REQUIRE(result.header.sequence == valid_header.sequence);
  BEACON_TEST_REQUIRE(result.header.presentation_time_us == valid_header.presentation_time_us);
  BEACON_TEST_REQUIRE(result.header.frame_bytes == valid_header.frame_bytes);
  BEACON_TEST_REQUIRE(result.payload.size() == 3);
  BEACON_TEST_REQUIRE(result.payload.data() == expected_datagram.data() + 40);
}

void malformed_datagrams_are_rejected() {
  auto malformed = expected_datagram;
  malformed[0] = std::byte{0};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_magic);

  malformed = expected_datagram;
  malformed[4] = std::byte{2};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::unsupported_version);

  malformed = expected_datagram;
  malformed[7] = std::byte{0x80};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_flags);

  malformed = expected_datagram;
  malformed[39] = std::byte{1};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::reserved_not_zero);

  malformed = expected_datagram;
  malformed[24] = std::byte{0x01};
  malformed[27] = std::byte{0x01};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_frame_size);

  malformed = expected_datagram;
  malformed[30] = std::byte{0};
  malformed[31] = std::byte{0};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_chunk_count);

  malformed = expected_datagram;
  malformed[30] = std::byte{0};
  malformed[31] = std::byte{1};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::invalid_chunk_index);

  malformed = expected_datagram;
  malformed[34] = std::byte{0x10};
  malformed[35] = std::byte{0x00};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::payload_out_of_range);

  malformed = expected_datagram;
  malformed[37] = std::byte{2};
  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(malformed).error == MediaDatagramError::payload_size_mismatch);

  BEACON_TEST_REQUIRE(beacon::stream::parse_media_datagram(
             std::span<const std::byte>{expected_datagram.data(), expected_datagram.size() - 1})
             .error == MediaDatagramError::payload_size_mismatch);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    static_assert(beacon::stream::media_datagram_header_bytes == 40);
    serialization_uses_the_fixed_network_order_vector();
    parsing_returns_header_and_zero_copy_payload();
    malformed_datagrams_are_rejected();
  });
}
