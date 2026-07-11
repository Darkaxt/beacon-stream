#include "beacon/worker/synthetic_media_source.h"

#include "beacon/stream/media_datagram.h"

#include <algorithm>
#include <cstddef>
#include <span>

namespace beacon::worker {

std::vector<stream::TransportPacket>
SyntheticMediaSource::emit_access_unit_marker(
    std::uint64_t sequence, std::uint64_t presentation_time_us,
    std::uint16_t maximum_datagram_bytes) const {
  constexpr auto packet_bytes = stream::media_datagram_header_bytes +
                                synthetic_access_unit_marker_bytes.size();
  if (maximum_datagram_bytes < packet_bytes) {
    return {};
  }

  stream::MediaDatagramHeader header{
      .version = stream::media_datagram_version,
      .media_kind = stream::MediaKind::video,
      .flags = stream::MediaDatagramFlags::idr |
               stream::MediaDatagramFlags::end_of_access_unit,
      .sequence = sequence,
      .presentation_time_us = presentation_time_us,
      .frame_bytes = static_cast<std::uint32_t>(
          synthetic_access_unit_marker_bytes.size()),
      .chunk_index = 0,
      .chunk_count = 1,
      .payload_offset = 0,
      .payload_bytes = static_cast<std::uint16_t>(
          synthetic_access_unit_marker_bytes.size()),
  };
  std::vector<std::byte> datagram(packet_bytes);
  if (!stream::serialize_media_datagram_header(
          header,
          std::span<std::byte, stream::media_datagram_header_bytes>{
              datagram.data(), stream::media_datagram_header_bytes})) {
    return {};
  }
  std::ranges::copy(synthetic_access_unit_marker_bytes,
                    datagram.begin() + stream::media_datagram_header_bytes);
  return {{.channel = stream::StreamChannel::media,
           .sequence = sequence,
           .payload = std::move(datagram)}};
}

} // namespace beacon::worker
