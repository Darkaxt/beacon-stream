#include "beacon/stream/media_datagram.h"
#include "beacon/worker/synthetic_media_source.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>

namespace {

constexpr std::uint64_t expected_timestamp_us = 1'000'000;
constexpr auto expected_payload =
    beacon::worker::synthetic_access_unit_marker_bytes;

void non_decodable_access_unit_marker_has_protocol_idr_flags() {
  const beacon::worker::SyntheticMediaSource source;
  const auto packets =
      source.emit_access_unit_marker(1, expected_timestamp_us, 1232);

  BEACON_TEST_REQUIRE(packets.size() == 1);
  BEACON_TEST_REQUIRE(packets[0].channel ==
                      beacon::stream::StreamChannel::media);
  BEACON_TEST_REQUIRE(packets[0].sequence == 1);
  BEACON_TEST_REQUIRE(packets[0].payload.size() <= 1232);

  const auto parsed =
      beacon::stream::parse_media_datagram(packets[0].payload);
  BEACON_TEST_REQUIRE(parsed.error == beacon::stream::MediaDatagramError::none);
  BEACON_TEST_REQUIRE(parsed.header.sequence == 1);
  BEACON_TEST_REQUIRE(parsed.header.presentation_time_us ==
                      expected_timestamp_us);
  BEACON_TEST_REQUIRE(parsed.header.chunk_index == 0);
  BEACON_TEST_REQUIRE(parsed.header.chunk_count == 1);
  BEACON_TEST_REQUIRE(
      parsed.header.flags ==
      (beacon::stream::MediaDatagramFlags::idr |
       beacon::stream::MediaDatagramFlags::end_of_access_unit));
  BEACON_TEST_REQUIRE(parsed.payload.size() == expected_payload.size());
  BEACON_TEST_REQUIRE(std::ranges::equal(parsed.payload, expected_payload));
  BEACON_TEST_REQUIRE(
      beacon::worker::synthetic_access_unit_diagnostic_name ==
      "gate3-non-decodable-access-unit-marker");
  BEACON_TEST_REQUIRE(!(parsed.payload.size() >= 4 &&
                        parsed.payload[0] == std::byte{0x00} &&
                        parsed.payload[1] == std::byte{0x00} &&
                        parsed.payload[2] == std::byte{0x01}));
}

void negotiated_limit_too_small_fails_without_partial_packets() {
  const beacon::worker::SyntheticMediaSource source;

  BEACON_TEST_REQUIRE(
      source
          .emit_access_unit_marker(
              1, expected_timestamp_us,
              static_cast<std::uint16_t>(
                  beacon::stream::media_datagram_header_bytes +
                  expected_payload.size() - 1))
          .empty());
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    non_decodable_access_unit_marker_has_protocol_idr_flags();
    negotiated_limit_too_small_fails_without_partial_packets();
  });
}
