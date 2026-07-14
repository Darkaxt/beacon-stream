#pragma once

#include "beacon/stream/transport.h"

#include <cstdint>
#include <span>
#include <vector>

namespace beacon::stream {

struct EncodedVideoAccessUnitView {
  std::span<const std::uint8_t> bytes;
  bool idr{};
  bool codec_configuration{};
};

enum class VideoMediaPacketizerFailure {
  none,
  invalid_sequence,
  empty_access_unit,
  access_unit_too_large,
  maximum_datagram_too_small,
  too_many_chunks,
  header_serialization_failed,
  resource_exhausted,
};

struct VideoMediaPacketizationResult {
  VideoMediaPacketizerFailure failure{VideoMediaPacketizerFailure::none};
  std::vector<TransportPacket> packets;
};

class VideoMediaPacketizer final {
public:
  [[nodiscard]] VideoMediaPacketizationResult packetize(
      EncodedVideoAccessUnitView access_unit, std::uint64_t sequence,
      std::uint64_t presentation_time_us,
      std::uint16_t maximum_datagram_bytes) const noexcept;
};

} // namespace beacon::stream
