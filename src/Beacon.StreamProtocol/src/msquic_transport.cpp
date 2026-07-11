#include "beacon/stream/msquic_transport.h"

#include <utility>

namespace beacon::stream {
namespace {

constexpr std::string_view kBeaconAlpn{"beacon-stream/1"};
constexpr std::uint64_t kAlpnMismatchError{1};

}  // namespace

bool MsQuicTransportState::connected(std::string_view negotiated_alpn) {
  if (state_ != MsQuicConnectionState::idle || negotiated_alpn != kBeaconAlpn) {
    state_ = MsQuicConnectionState::failed;
    record(MsQuicEventKind::transport_failed, 0, kAlpnMismatchError);
    return false;
  }

  state_ = MsQuicConnectionState::connected;
  record(MsQuicEventKind::connected);
  return true;
}

void MsQuicTransportState::datagram_state_changed(bool send_enabled,
                                                  std::uint16_t maximum_send_length) {
  datagram_send_enabled_ = send_enabled && maximum_send_length > 0;
  maximum_datagram_bytes_ = datagram_send_enabled_ ? maximum_send_length : 0;
  record(datagram_send_enabled_ ? MsQuicEventKind::datagram_ready
                                : MsQuicEventKind::datagram_unavailable,
         0, 0, maximum_datagram_bytes_);
}

void MsQuicTransportState::datagram_send_state_changed(
    std::uint64_t sequence,
    QUIC_DATAGRAM_SEND_STATE state) {
  switch (state) {
    case QUIC_DATAGRAM_SEND_SENT:
      record(MsQuicEventKind::datagram_sent, sequence);
      break;
    case QUIC_DATAGRAM_SEND_LOST_DISCARDED:
      record(MsQuicEventKind::datagram_lost, sequence);
      break;
    case QUIC_DATAGRAM_SEND_ACKNOWLEDGED:
    case QUIC_DATAGRAM_SEND_ACKNOWLEDGED_SPURIOUS:
      record(MsQuicEventKind::datagram_acknowledged, sequence);
      break;
    case QUIC_DATAGRAM_SEND_CANCELED:
      record(MsQuicEventKind::datagram_canceled, sequence);
      break;
    case QUIC_DATAGRAM_SEND_UNKNOWN:
    case QUIC_DATAGRAM_SEND_LOST_SUSPECT:
      break;
  }
}

void MsQuicTransportState::peer_closed(std::uint64_t error_code) {
  if (state_ != MsQuicConnectionState::failed) {
    state_ = MsQuicConnectionState::closing;
  }
  record(MsQuicEventKind::peer_closed, 0, error_code);
}

void MsQuicTransportState::transport_failed(QUIC_STATUS status,
                                            std::uint64_t error_code) {
  state_ = MsQuicConnectionState::failed;
  const auto status_code = static_cast<std::uint64_t>(static_cast<std::uint32_t>(status));
  record(MsQuicEventKind::transport_failed, 0,
         error_code == 0 ? status_code : error_code);
}

void MsQuicTransportState::closed() {
  if (state_ != MsQuicConnectionState::failed) {
    state_ = MsQuicConnectionState::closed;
  }
  datagram_send_enabled_ = false;
  maximum_datagram_bytes_ = 0;
  record(MsQuicEventKind::closed);
}

MsQuicConnectionState MsQuicTransportState::state() const noexcept { return state_; }

bool MsQuicTransportState::datagram_send_enabled() const noexcept {
  return datagram_send_enabled_;
}

std::uint16_t MsQuicTransportState::maximum_datagram_bytes() const noexcept {
  return maximum_datagram_bytes_;
}

std::vector<MsQuicTransportEvent> MsQuicTransportState::take_events() {
  auto result = std::move(events_);
  events_.clear();
  return result;
}

void MsQuicTransportState::record(MsQuicEventKind kind,
                                  std::uint64_t sequence,
                                  std::uint64_t error_code,
                                  std::uint16_t maximum_datagram_bytes) {
  events_.push_back({.kind = kind,
                     .sequence = sequence,
                     .error_code = error_code,
                     .maximum_datagram_bytes = maximum_datagram_bytes});
}

}  // namespace beacon::stream
