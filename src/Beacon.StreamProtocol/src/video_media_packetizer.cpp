#include "beacon/stream/video_media_packetizer.h"

#include "beacon/stream/media_datagram.h"

#include <algorithm>
#include <cstddef>
#include <limits>
#include <new>
#include <span>

namespace beacon::stream {
namespace {

VideoMediaPacketizationResult
failure_result(VideoMediaPacketizerFailure failure) noexcept {
  return {.failure = failure, .packets = {}};
}

MediaDatagramFlags
access_unit_flags(EncodedVideoAccessUnitView access_unit) noexcept {
  auto flags = MediaDatagramFlags::none;
  if (access_unit.idr) {
    flags = flags | MediaDatagramFlags::idr;
  }
  if (access_unit.codec_configuration) {
    flags = flags | MediaDatagramFlags::codec_configuration;
  }
  return flags;
}

} // namespace

VideoMediaPacketizationResult VideoMediaPacketizer::packetize(
    EncodedVideoAccessUnitView access_unit, std::uint64_t sequence,
    std::uint64_t presentation_time_us,
    std::uint16_t maximum_datagram_bytes) const noexcept {
  if (sequence == 0) {
    return failure_result(VideoMediaPacketizerFailure::invalid_sequence);
  }
  if (access_unit.bytes.empty()) {
    return failure_result(VideoMediaPacketizerFailure::empty_access_unit);
  }
  if (access_unit.bytes.size() > maximum_media_frame_bytes) {
    return failure_result(VideoMediaPacketizerFailure::access_unit_too_large);
  }
  if (maximum_datagram_bytes <= media_datagram_header_bytes) {
    return failure_result(
        VideoMediaPacketizerFailure::maximum_datagram_too_small);
  }

  const auto payload_capacity = static_cast<std::size_t>(
      maximum_datagram_bytes - media_datagram_header_bytes);
  const auto chunk_count =
      (access_unit.bytes.size() + payload_capacity - 1U) / payload_capacity;
  if (chunk_count > std::numeric_limits<std::uint16_t>::max()) {
    return failure_result(VideoMediaPacketizerFailure::too_many_chunks);
  }

  try {
    VideoMediaPacketizationResult result;
    result.packets.reserve(chunk_count);
    const auto stable_flags = access_unit_flags(access_unit);
    for (std::size_t index = 0; index < chunk_count; ++index) {
      const auto offset = index * payload_capacity;
      const auto payload_bytes =
          std::min(payload_capacity, access_unit.bytes.size() - offset);
      auto flags = stable_flags;
      if (index + 1U == chunk_count) {
        flags = flags | MediaDatagramFlags::end_of_access_unit;
      }

      std::vector<std::byte> datagram(media_datagram_header_bytes +
                                      payload_bytes);
      const MediaDatagramHeader header{
          .version = media_datagram_version,
          .media_kind = MediaKind::video,
          .flags = flags,
          .sequence = sequence,
          .presentation_time_us = presentation_time_us,
          .frame_bytes = static_cast<std::uint32_t>(access_unit.bytes.size()),
          .chunk_index = static_cast<std::uint16_t>(index),
          .chunk_count = static_cast<std::uint16_t>(chunk_count),
          .payload_offset = static_cast<std::uint32_t>(offset),
          .payload_bytes = static_cast<std::uint16_t>(payload_bytes),
      };
      if (!serialize_media_datagram_header(
              header,
              std::span<std::byte, media_datagram_header_bytes>{
                  datagram.data(), media_datagram_header_bytes})) {
        return failure_result(
            VideoMediaPacketizerFailure::header_serialization_failed);
      }
      std::ranges::transform(
          access_unit.bytes.begin() + static_cast<std::ptrdiff_t>(offset),
          access_unit.bytes.begin() +
              static_cast<std::ptrdiff_t>(offset + payload_bytes),
          datagram.begin() + media_datagram_header_bytes,
          [](std::uint8_t value) { return static_cast<std::byte>(value); });
      result.packets.push_back({
          .channel = StreamChannel::media,
          .sequence = sequence,
          .payload = std::move(datagram),
      });
    }
    return result;
  } catch (const std::bad_alloc &) {
    return failure_result(VideoMediaPacketizerFailure::resource_exhausted);
  } catch (...) {
    return failure_result(VideoMediaPacketizerFailure::resource_exhausted);
  }
}

} // namespace beacon::stream
