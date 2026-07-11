#include "fake_transport.h"

#include <utility>

namespace beacon::stream::testing {

bool FakeTransport::open_connection() {
  if (shutdown_ || connection_open_) {
    return false;
  }
  connection_open_ = true;
  ++open_count_;
  events_.push_back({TransportEventKind::connection_opened, 0});
  return true;
}

void FakeTransport::close_connection() noexcept {
  if (!connection_open_) {
    return;
  }
  connection_open_ = false;
  ++close_count_;
  events_.push_back({TransportEventKind::connection_closed, 0});
}

TransportSendResult FakeTransport::send(TransportPacket packet) {
  if (!connection_open_ || shutdown_) {
    return TransportSendResult::connection_closed;
  }

  const auto fault = faults_.contains(packet.sequence) ? faults_.at(packet.sequence)
                                                        : FaultAction::deliver;
  switch (fault) {
    case FaultAction::drop:
      events_.push_back({TransportEventKind::packet_dropped, packet.sequence});
      return TransportSendResult::accepted;
    case FaultAction::duplicate:
      delivered_packets_.push_back(packet);
      delivered_packets_.push_back(std::move(packet));
      events_.push_back({TransportEventKind::packet_duplicated,
                         delivered_packets_.back().sequence});
      return TransportSendResult::accepted;
    case FaultAction::hold:
      events_.push_back({TransportEventKind::packet_held, packet.sequence});
      held_packets_.push_back(std::move(packet));
      return TransportSendResult::accepted;
    case FaultAction::deliver:
      events_.push_back({TransportEventKind::packet_delivered, packet.sequence});
      delivered_packets_.push_back(std::move(packet));
      return TransportSendResult::accepted;
  }
  return TransportSendResult::connection_closed;
}

void FakeTransport::shutdown() noexcept {
  if (shutdown_) {
    return;
  }
  close_connection();
  shutdown_ = true;
  ++shutdown_count_;
  events_.push_back({TransportEventKind::transport_shutdown, 0});
}

void FakeTransport::set_fault(std::uint64_t sequence, FaultAction action) {
  faults_.insert_or_assign(sequence, action);
}

void FakeTransport::release_held() {
  for (auto& packet : held_packets_) {
    events_.push_back({TransportEventKind::packet_delivered, packet.sequence});
    delivered_packets_.push_back(std::move(packet));
  }
  held_packets_.clear();
}

const std::vector<TransportPacket>& FakeTransport::delivered_packets() const noexcept {
  return delivered_packets_;
}

const std::vector<TransportEvent>& FakeTransport::events() const noexcept { return events_; }

std::size_t FakeTransport::open_count() const noexcept { return open_count_; }

std::size_t FakeTransport::close_count() const noexcept { return close_count_; }

std::size_t FakeTransport::shutdown_count() const noexcept { return shutdown_count_; }

}  // namespace beacon::stream::testing
