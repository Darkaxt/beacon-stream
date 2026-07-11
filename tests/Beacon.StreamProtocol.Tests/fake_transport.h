#pragma once

#include "beacon/stream/transport.h"

#include <cstddef>
#include <cstdint>
#include <unordered_map>
#include <vector>

namespace beacon::stream::testing {

enum class FaultAction {
  deliver,
  drop,
  duplicate,
  hold,
};

class FakeTransport final : public IStreamTransport {
 public:
  bool open_connection() override;
  void close_connection() noexcept override;
  TransportSendResult send(TransportPacket packet) override;
  void shutdown() noexcept override;

  void set_fault(std::uint64_t sequence, FaultAction action);
  void release_held();

  [[nodiscard]] const std::vector<TransportPacket>& delivered_packets() const noexcept;
  [[nodiscard]] const std::vector<TransportEvent>& events() const noexcept;
  [[nodiscard]] std::size_t open_count() const noexcept;
  [[nodiscard]] std::size_t close_count() const noexcept;
  [[nodiscard]] std::size_t shutdown_count() const noexcept;

 private:
  bool connection_open_{};
  bool shutdown_{};
  std::size_t open_count_{};
  std::size_t close_count_{};
  std::size_t shutdown_count_{};
  std::unordered_map<std::uint64_t, FaultAction> faults_;
  std::vector<TransportPacket> held_packets_;
  std::vector<TransportPacket> delivered_packets_;
  std::vector<TransportEvent> events_;
};

}  // namespace beacon::stream::testing
