#pragma once

#include <msquic.h>

#include <cstdint>
#include <string_view>
#include <vector>

namespace beacon::stream {

enum class MsQuicConnectionState {
  idle,
  connected,
  closing,
  closed,
  failed,
};

enum class MsQuicEventKind {
  connected,
  datagram_ready,
  datagram_unavailable,
  datagram_sent,
  datagram_acknowledged,
  datagram_lost,
  datagram_canceled,
  peer_closed,
  transport_failed,
  closed,
};

struct MsQuicTransportEvent {
  MsQuicEventKind kind{};
  std::uint64_t sequence{};
  std::uint64_t error_code{};
  std::uint16_t maximum_datagram_bytes{};
};

class MsQuicTransportState {
 public:
  [[nodiscard]] bool connected(std::string_view negotiated_alpn);
  void datagram_state_changed(bool send_enabled, std::uint16_t maximum_send_length);
  void datagram_send_state_changed(std::uint64_t sequence, QUIC_DATAGRAM_SEND_STATE state);
  void peer_closed(std::uint64_t error_code);
  void transport_failed(QUIC_STATUS status, std::uint64_t error_code);
  void closed();

  [[nodiscard]] MsQuicConnectionState state() const noexcept;
  [[nodiscard]] bool datagram_send_enabled() const noexcept;
  [[nodiscard]] std::uint16_t maximum_datagram_bytes() const noexcept;
  [[nodiscard]] std::vector<MsQuicTransportEvent> take_events();

 private:
  void record(MsQuicEventKind kind,
              std::uint64_t sequence = 0,
              std::uint64_t error_code = 0,
              std::uint16_t maximum_datagram_bytes = 0);

  MsQuicConnectionState state_{MsQuicConnectionState::idle};
  bool datagram_send_enabled_{};
  std::uint16_t maximum_datagram_bytes_{};
  std::vector<MsQuicTransportEvent> events_;
};

}  // namespace beacon::stream
