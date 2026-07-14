#pragma once

#include "beacon/stream/transport.h"

#include <cstdint>

namespace beacon::worker {

class IMediaDatagramTransport {
public:
  virtual ~IMediaDatagramTransport() = default;

  [[nodiscard]] virtual stream::TransportSendResult
  send_for_generation(stream::TransportPacket packet,
                      std::uint64_t session_generation) = 0;
};

} // namespace beacon::worker
