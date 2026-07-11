#include "beacon/stream/media_datagram.h"
#include "beacon/worker/synthetic_media_source.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>

namespace {

constexpr std::uint64_t expected_timestamp_us = 1'000'000;
constexpr std::array expected_payload{std::byte{0x00}, std::byte{0x00},
                                      std::byte{0x01}, std::byte{0x65}};

void deterministic_idr_is_one_parseable_bounded_datagram() {
  const beacon::worker::SyntheticMediaSource source;
  const auto packets = source.emit_idr(1, expected_timestamp_us, 1232);

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
}

void negotiated_limit_too_small_fails_without_partial_packets() {
  const beacon::worker::SyntheticMediaSource source;

  BEACON_TEST_REQUIRE(
      source
          .emit_idr(1, expected_timestamp_us,
                    static_cast<std::uint16_t>(
                        beacon::stream::media_datagram_header_bytes +
                        expected_payload.size() - 1))
          .empty());
}

} // namespace

int main() {
  deterministic_idr_is_one_parseable_bounded_datagram();
  negotiated_limit_too_small_fails_without_partial_packets();
  return 0;
}
