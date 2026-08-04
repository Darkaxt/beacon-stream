#include "beacon/stream/audio_media_packetizer.h"
#include "beacon/stream/media_datagram.h"

#include "test_failure.h"

#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

namespace stream = beacon::stream;

std::vector<std::uint8_t> packet(std::size_t size) {
  std::vector<std::uint8_t> result(size);
  for (std::size_t index = 0; index < size; ++index) {
    result[index] = static_cast<std::uint8_t>(index % 251U);
  }
  return result;
}

void one_opus_packet_maps_to_one_audio_datagram() {
  const stream::AudioMediaPacketizer packetizer;
  const auto encoded = packet(240);

  const auto result = packetizer.packetize(encoded, 17, 980'000, 1232);

  BEACON_TEST_REQUIRE(result.failure ==
                      stream::AudioMediaPacketizerFailure::none);
  BEACON_TEST_REQUIRE(result.packet.has_value());
  BEACON_TEST_REQUIRE(result.packet->channel == stream::StreamChannel::media);
  BEACON_TEST_REQUIRE(result.packet->sequence == 17);
  const auto parsed = stream::parse_media_datagram(result.packet->payload);
  BEACON_TEST_REQUIRE(parsed.error == stream::MediaDatagramError::none);
  BEACON_TEST_REQUIRE(parsed.header.media_kind == stream::MediaKind::audio);
  BEACON_TEST_REQUIRE(parsed.header.sequence == 17);
  BEACON_TEST_REQUIRE(parsed.header.presentation_time_us == 980'000);
  BEACON_TEST_REQUIRE(parsed.header.frame_bytes == encoded.size());
  BEACON_TEST_REQUIRE(parsed.header.chunk_index == 0);
  BEACON_TEST_REQUIRE(parsed.header.chunk_count == 1);
  BEACON_TEST_REQUIRE(parsed.header.payload_offset == 0);
  BEACON_TEST_REQUIRE(parsed.header.payload_bytes == encoded.size());
  BEACON_TEST_REQUIRE(parsed.header.flags ==
                      stream::MediaDatagramFlags::end_of_access_unit);
  BEACON_TEST_REQUIRE(parsed.payload.size() == encoded.size());
  for (std::size_t index = 0; index < encoded.size(); ++index) {
    BEACON_TEST_REQUIRE(parsed.payload[index] ==
                        static_cast<std::byte>(encoded[index]));
  }
}

void invalid_or_oversized_packets_fail_without_output() {
  const stream::AudioMediaPacketizer packetizer;
  const auto encoded = packet(240);

  BEACON_TEST_REQUIRE(packetizer.packetize({}, 1, 0, 1232).failure ==
                      stream::AudioMediaPacketizerFailure::empty_packet);
  BEACON_TEST_REQUIRE(packetizer.packetize(encoded, 0, 0, 1232).failure ==
                      stream::AudioMediaPacketizerFailure::invalid_sequence);
  BEACON_TEST_REQUIRE(
      packetizer
          .packetize(
              encoded, 1, 0,
              static_cast<std::uint16_t>(stream::media_datagram_header_bytes))
          .failure ==
      stream::AudioMediaPacketizerFailure::maximum_datagram_too_small);
  BEACON_TEST_REQUIRE(
      packetizer
          .packetize(encoded, 1, 0,
                     static_cast<std::uint16_t>(
                         stream::media_datagram_header_bytes + 239U))
          .failure == stream::AudioMediaPacketizerFailure::packet_too_large);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    one_opus_packet_maps_to_one_audio_datagram();
    invalid_or_oversized_packets_fail_without_output();
  });
}
