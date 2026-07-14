#include "beacon/worker/video/media_packetizer.h"

#include "beacon/stream/media_datagram.h"

#include <algorithm>
#include <cstddef>
#include <limits>
#include <new>
#include <span>

namespace beacon::worker::video {
namespace {

stream::MediaDatagramFlags
access_unit_flags(const EncodedH264AccessUnit &access_unit) noexcept {
  auto flags = stream::MediaDatagramFlags::none;
  if (access_unit.idr) {
    flags = flags | stream::MediaDatagramFlags::idr;
  }
  if (access_unit.has_sps || access_unit.has_pps) {
    flags = flags | stream::MediaDatagramFlags::codec_configuration;
  }
  return flags;
}

} // namespace

MediaPacketizationResult MediaPacketizer::packetize(
    const EncodedH264AccessUnit &access_unit, std::uint64_t sequence,
    std::uint64_t presentation_time_us,
    std::uint16_t maximum_datagram_bytes) const noexcept {
  if (sequence == 0) {
    return {.failure = MediaPacketizerFailure::invalid_sequence};
  }
  if (access_unit.annex_b.empty()) {
    return {.failure = MediaPacketizerFailure::empty_access_unit};
  }
  if (access_unit.annex_b.size() > stream::maximum_media_frame_bytes) {
    return {.failure = MediaPacketizerFailure::access_unit_too_large};
  }
  if (maximum_datagram_bytes <= stream::media_datagram_header_bytes) {
    return {.failure = MediaPacketizerFailure::maximum_datagram_too_small};
  }

  const auto payload_capacity = static_cast<std::size_t>(
      maximum_datagram_bytes - stream::media_datagram_header_bytes);
  const auto chunk_count =
      (access_unit.annex_b.size() + payload_capacity - 1U) / payload_capacity;
  if (chunk_count > std::numeric_limits<std::uint16_t>::max()) {
    return {.failure = MediaPacketizerFailure::too_many_chunks};
  }

  try {
    MediaPacketizationResult result;
    result.packets.reserve(chunk_count);
    const auto stable_flags = access_unit_flags(access_unit);
    for (std::size_t index = 0; index < chunk_count; ++index) {
      const auto offset = index * payload_capacity;
      const auto payload_bytes =
          std::min(payload_capacity, access_unit.annex_b.size() - offset);
      auto flags = stable_flags;
      if (index + 1U == chunk_count) {
        flags = flags | stream::MediaDatagramFlags::end_of_access_unit;
      }

      std::vector<std::byte> datagram(stream::media_datagram_header_bytes +
                                      payload_bytes);
      const stream::MediaDatagramHeader header{
          .version = stream::media_datagram_version,
          .media_kind = stream::MediaKind::video,
          .flags = flags,
          .sequence = sequence,
          .presentation_time_us = presentation_time_us,
          .frame_bytes = static_cast<std::uint32_t>(access_unit.annex_b.size()),
          .chunk_index = static_cast<std::uint16_t>(index),
          .chunk_count = static_cast<std::uint16_t>(chunk_count),
          .payload_offset = static_cast<std::uint32_t>(offset),
          .payload_bytes = static_cast<std::uint16_t>(payload_bytes),
      };
      if (!stream::serialize_media_datagram_header(
              header,
              std::span<std::byte, stream::media_datagram_header_bytes>{
                  datagram.data(), stream::media_datagram_header_bytes})) {
        return {.failure = MediaPacketizerFailure::header_serialization_failed};
      }
      std::ranges::transform(
          access_unit.annex_b.begin() + static_cast<std::ptrdiff_t>(offset),
          access_unit.annex_b.begin() +
              static_cast<std::ptrdiff_t>(offset + payload_bytes),
          datagram.begin() + stream::media_datagram_header_bytes,
          [](std::uint8_t value) { return static_cast<std::byte>(value); });
      result.packets.push_back({
          .channel = stream::StreamChannel::media,
          .sequence = sequence,
          .payload = std::move(datagram),
      });
    }
    return result;
  } catch (const std::bad_alloc &) {
    return {.failure = MediaPacketizerFailure::resource_exhausted};
  } catch (...) {
    return {.failure = MediaPacketizerFailure::resource_exhausted};
  }
}

} // namespace beacon::worker::video
