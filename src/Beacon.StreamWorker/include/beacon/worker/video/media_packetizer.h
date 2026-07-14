#pragma once

#include "beacon/stream/transport.h"
#include "beacon/worker/video/nvenc_h264_encoder.h"

#include <cstdint>
#include <vector>

namespace beacon::worker::video {

enum class MediaPacketizerFailure {
  none,
  invalid_sequence,
  empty_access_unit,
  access_unit_too_large,
  maximum_datagram_too_small,
  too_many_chunks,
  header_serialization_failed,
  resource_exhausted,
};

struct MediaPacketizationResult {
  MediaPacketizerFailure failure{MediaPacketizerFailure::none};
  std::vector<stream::TransportPacket> packets;
};

class MediaPacketizer final {
public:
  [[nodiscard]] MediaPacketizationResult
  packetize(const EncodedH264AccessUnit &access_unit, std::uint64_t sequence,
            std::uint64_t presentation_time_us,
            std::uint16_t maximum_datagram_bytes) const noexcept;
};

} // namespace beacon::worker::video
