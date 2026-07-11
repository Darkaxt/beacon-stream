#pragma once

#include <cstddef>
#include <cstdint>
#include <vector>

namespace beacon::stream {

enum class StreamChannel {
  session,
  input,
  feedback,
  media,
};

struct TransportPacket {
  StreamChannel channel{StreamChannel::session};
  std::uint64_t sequence{};
  std::vector<std::byte> payload;
};

enum class TransportSendResult {
  accepted,
  connection_closed,
};

enum class TransportEventKind {
  connection_opened,
  connection_closed,
  packet_delivered,
  packet_dropped,
  packet_duplicated,
  packet_held,
  transport_shutdown,
};

struct TransportEvent {
  TransportEventKind kind{};
  std::uint64_t sequence{};
};

class IStreamTransport {
 public:
  virtual ~IStreamTransport() = default;

  [[nodiscard]] virtual bool open_connection() = 0;
  virtual void close_connection() noexcept = 0;
  [[nodiscard]] virtual TransportSendResult send(TransportPacket packet) = 0;
  virtual void shutdown() noexcept = 0;
};

}  // namespace beacon::stream
