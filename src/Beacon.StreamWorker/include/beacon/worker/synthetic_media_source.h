#pragma once

#include "beacon/stream/transport.h"

#include <cstdint>
#include <vector>

namespace beacon::worker {

class SyntheticMediaSource final {
public:
  [[nodiscard]] std::vector<stream::TransportPacket>
  emit_idr(std::uint64_t sequence, std::uint64_t presentation_time_us,
           std::uint16_t maximum_datagram_bytes) const;
};

} // namespace beacon::worker
