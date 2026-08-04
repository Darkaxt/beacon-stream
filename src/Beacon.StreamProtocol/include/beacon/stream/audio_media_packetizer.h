#pragma once

#include "beacon/stream/transport.h"

#include <cstdint>
#include <optional>
#include <span>

namespace beacon::stream {

enum class AudioMediaPacketizerFailure {
  none,
  invalid_sequence,
  empty_packet,
  maximum_datagram_too_small,
  packet_too_large,
  header_serialization_failed,
  resource_exhausted,
};

struct AudioMediaPacketizationResult {
  AudioMediaPacketizerFailure failure{AudioMediaPacketizerFailure::none};
  std::optional<TransportPacket> packet;
};

class AudioMediaPacketizer final {
public:
  [[nodiscard]] AudioMediaPacketizationResult
  packetize(std::span<const std::uint8_t> encoded_packet,
            std::uint64_t sequence, std::uint64_t presentation_time_us,
            std::uint16_t maximum_datagram_bytes) const noexcept;
};

} // namespace beacon::stream
