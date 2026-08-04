#include "beacon/stream/audio_media_packetizer.h"

#include "beacon/stream/media_datagram.h"

#include <algorithm>
#include <cstddef>
#include <limits>
#include <new>
#include <vector>

namespace beacon::stream {
namespace {

AudioMediaPacketizationResult
failure_result(AudioMediaPacketizerFailure failure) noexcept {
  return {.failure = failure, .packet = std::nullopt};
}

} // namespace

AudioMediaPacketizationResult AudioMediaPacketizer::packetize(
    std::span<const std::uint8_t> encoded_packet, std::uint64_t sequence,
    std::uint64_t presentation_time_us,
    std::uint16_t maximum_datagram_bytes) const noexcept {
  if (sequence == 0) {
    return failure_result(AudioMediaPacketizerFailure::invalid_sequence);
  }
  if (encoded_packet.empty()) {
    return failure_result(AudioMediaPacketizerFailure::empty_packet);
  }
  if (maximum_datagram_bytes <= media_datagram_header_bytes) {
    return failure_result(
        AudioMediaPacketizerFailure::maximum_datagram_too_small);
  }
  const auto payload_capacity = static_cast<std::size_t>(
      maximum_datagram_bytes - media_datagram_header_bytes);
  if (encoded_packet.size() > payload_capacity ||
      encoded_packet.size() > std::numeric_limits<std::uint16_t>::max()) {
    return failure_result(AudioMediaPacketizerFailure::packet_too_large);
  }

  try {
    std::vector<std::byte> datagram(media_datagram_header_bytes +
                                    encoded_packet.size());
    const MediaDatagramHeader header{
        .version = media_datagram_version,
        .media_kind = MediaKind::audio,
        .flags = MediaDatagramFlags::end_of_access_unit,
        .sequence = sequence,
        .presentation_time_us = presentation_time_us,
        .frame_bytes = static_cast<std::uint32_t>(encoded_packet.size()),
        .chunk_index = 0,
        .chunk_count = 1,
        .payload_offset = 0,
        .payload_bytes = static_cast<std::uint16_t>(encoded_packet.size()),
    };
    if (!serialize_media_datagram_header(
            header, std::span<std::byte, media_datagram_header_bytes>{
                        datagram.data(), media_datagram_header_bytes})) {
      return failure_result(
          AudioMediaPacketizerFailure::header_serialization_failed);
    }
    std::ranges::transform(
        encoded_packet, datagram.begin() + media_datagram_header_bytes,
        [](std::uint8_t value) { return static_cast<std::byte>(value); });
    return {
        .failure = AudioMediaPacketizerFailure::none,
        .packet =
            TransportPacket{
                .channel = StreamChannel::media,
                .sequence = sequence,
                .payload = std::move(datagram),
            },
    };
  } catch (const std::bad_alloc &) {
    return failure_result(AudioMediaPacketizerFailure::resource_exhausted);
  } catch (...) {
    return failure_result(AudioMediaPacketizerFailure::resource_exhausted);
  }
}

} // namespace beacon::stream
