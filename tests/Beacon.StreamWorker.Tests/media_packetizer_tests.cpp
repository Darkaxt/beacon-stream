#include "beacon/stream/frame_assembler.h"
#include "beacon/stream/media_datagram.h"
#include "beacon/worker/video/media_packetizer.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <limits>
#include <vector>

namespace {

namespace stream = beacon::stream;
namespace video = beacon::worker::video;

bool has_flag(stream::MediaDatagramFlags value,
              stream::MediaDatagramFlags flag) {
  return (static_cast<std::uint16_t>(value) &
          static_cast<std::uint16_t>(flag)) != 0;
}

video::EncodedH264AccessUnit access_unit(std::size_t bytes, bool idr = false,
                                         bool has_sps = false,
                                         bool has_pps = false) {
  video::EncodedH264AccessUnit result;
  result.annex_b.resize(bytes);
  for (std::size_t index = 0; index < bytes; ++index) {
    result.annex_b[index] = static_cast<std::uint8_t>(index % 251U);
  }
  result.qpc_timestamp = 123'456;
  result.idr = idr;
  result.has_sps = has_sps;
  result.has_pps = has_pps;
  return result;
}

std::vector<std::byte> as_bytes(const video::EncodedH264AccessUnit &unit) {
  std::vector<std::byte> result(unit.annex_b.size());
  std::ranges::transform(unit.annex_b, result.begin(), [](std::uint8_t value) {
    return static_cast<std::byte>(value);
  });
  return result;
}

void negotiated_limit_drives_exact_chunks_and_reconstruction() {
  constexpr std::uint16_t maximum_datagram_bytes = 1232;
  constexpr std::size_t payload_capacity =
      maximum_datagram_bytes - stream::media_datagram_header_bytes;
  const auto unit = access_unit(payload_capacity * 2U + 73U);
  const video::MediaPacketizer packetizer;

  const auto result =
      packetizer.packetize(unit, 41, 987'654, maximum_datagram_bytes);

  BEACON_TEST_REQUIRE(result.failure == video::MediaPacketizerFailure::none);
  BEACON_TEST_REQUIRE(result.packets.size() == 3);
  for (std::size_t index = 0; index < result.packets.size(); ++index) {
    const auto &packet = result.packets[index];
    BEACON_TEST_REQUIRE(packet.channel == stream::StreamChannel::media);
    BEACON_TEST_REQUIRE(packet.sequence == 41);
    BEACON_TEST_REQUIRE(packet.payload.size() <= maximum_datagram_bytes);
    const auto parsed = stream::parse_media_datagram(packet.payload);
    BEACON_TEST_REQUIRE(parsed.error == stream::MediaDatagramError::none);
    BEACON_TEST_REQUIRE(parsed.header.sequence == 41);
    BEACON_TEST_REQUIRE(parsed.header.presentation_time_us == 987'654);
    BEACON_TEST_REQUIRE(parsed.header.frame_bytes == unit.annex_b.size());
    BEACON_TEST_REQUIRE(parsed.header.chunk_index == index);
    BEACON_TEST_REQUIRE(parsed.header.chunk_count == 3);
    BEACON_TEST_REQUIRE(parsed.header.payload_offset ==
                        index * payload_capacity);
    BEACON_TEST_REQUIRE(
        has_flag(parsed.header.flags,
                 stream::MediaDatagramFlags::end_of_access_unit) ==
        (index + 1U == result.packets.size()));
  }

  stream::FrameAssembler assembler(stream::maximum_media_frame_bytes);
  std::optional<stream::CompletedFrame> completed;
  for (auto packet = result.packets.rbegin(); packet != result.packets.rend();
       ++packet) {
    auto pushed = assembler.push(packet->payload);
    if (pushed.frame.has_value()) {
      completed = std::move(pushed.frame);
    }
  }
  BEACON_TEST_REQUIRE(completed.has_value());
  BEACON_TEST_REQUIRE(completed->sequence == 41);
  BEACON_TEST_REQUIRE(completed->presentation_time_us == 987'654);
  BEACON_TEST_REQUIRE(completed->bytes == as_bytes(unit));
}

void idr_and_codec_configuration_flags_are_stable_across_chunks() {
  constexpr std::uint16_t maximum_datagram_bytes = 96;
  const auto unit = access_unit(130, true, true, true);
  const video::MediaPacketizer packetizer;

  const auto result = packetizer.packetize(unit, 7, 55, maximum_datagram_bytes);

  BEACON_TEST_REQUIRE(result.failure == video::MediaPacketizerFailure::none);
  BEACON_TEST_REQUIRE(result.packets.size() == 3);
  for (std::size_t index = 0; index < result.packets.size(); ++index) {
    const auto parsed =
        stream::parse_media_datagram(result.packets[index].payload);
    BEACON_TEST_REQUIRE(parsed.error == stream::MediaDatagramError::none);
    BEACON_TEST_REQUIRE(
        has_flag(parsed.header.flags, stream::MediaDatagramFlags::idr));
    BEACON_TEST_REQUIRE(has_flag(
        parsed.header.flags, stream::MediaDatagramFlags::codec_configuration));
    BEACON_TEST_REQUIRE(
        has_flag(parsed.header.flags,
                 stream::MediaDatagramFlags::end_of_access_unit) ==
        (index + 1U == result.packets.size()));
  }
}

void invalid_contracts_fail_without_partial_packets() {
  const video::MediaPacketizer packetizer;
  const auto unit = access_unit(64);
  const video::EncodedH264AccessUnit empty;

  BEACON_TEST_REQUIRE(packetizer.packetize(empty, 1, 0, 1232).failure ==
                      video::MediaPacketizerFailure::empty_access_unit);
  BEACON_TEST_REQUIRE(packetizer.packetize(unit, 0, 0, 1232).failure ==
                      video::MediaPacketizerFailure::invalid_sequence);
  BEACON_TEST_REQUIRE(
      packetizer
          .packetize(
              unit, 1, 0,
              static_cast<std::uint16_t>(stream::media_datagram_header_bytes))
          .failure ==
      video::MediaPacketizerFailure::maximum_datagram_too_small);

  const auto too_large = access_unit(
      static_cast<std::size_t>(stream::maximum_media_frame_bytes) + 1U);
  BEACON_TEST_REQUIRE(packetizer.packetize(too_large, 1, 0, 1232).failure ==
                      video::MediaPacketizerFailure::access_unit_too_large);

  const auto too_many_chunks = access_unit(
      static_cast<std::size_t>(std::numeric_limits<std::uint16_t>::max()) + 1U);
  BEACON_TEST_REQUIRE(
      packetizer
          .packetize(too_many_chunks, 1, 0,
                     static_cast<std::uint16_t>(
                         stream::media_datagram_header_bytes + 1U))
          .failure == video::MediaPacketizerFailure::too_many_chunks);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    negotiated_limit_drives_exact_chunks_and_reconstruction();
    idr_and_codec_configuration_flags_are_stable_across_chunks();
    invalid_contracts_fail_without_partial_packets();
  });
}
