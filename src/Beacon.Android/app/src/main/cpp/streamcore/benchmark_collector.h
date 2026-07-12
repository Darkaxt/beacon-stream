#pragma once

#include "beacon/stream/benchmark_datagram.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>
#include <vector>

namespace beacon::android::streamcore {

struct BenchmarkCollectorPlan {
  std::array<std::byte, 16> run_token{};
  std::uint32_t reliable_packet_count{};
  std::uint32_t reliable_payload_bytes{};
  std::uint32_t datagram_packet_count{};
  std::uint32_t datagram_payload_bytes{};
};

struct BenchmarkRttObservation {
  std::uint64_t sequence{};
  std::uint64_t rtt_us{};
};

struct BenchmarkNetworkSample {
  std::uint64_t sequence{};
  std::uint32_t payload_bytes{};
  std::uint64_t rtt_us{};
  std::uint64_t jitter_us{};
  std::uint32_t reorder_distance{};
  bool received{};
};

struct BenchmarkCollectionResult {
  double sustainable_throughput_mbps{};
  std::uint32_t received_datagrams{};
  std::vector<BenchmarkNetworkSample> samples;
};

class BenchmarkCollector {
 public:
  [[nodiscard]] bool start(BenchmarkCollectorPlan plan);
  [[nodiscard]] bool observe_reliable(std::uint64_t sequence,
                                      std::uint32_t payload_bytes,
                                      std::uint64_t arrived_at_us);
  [[nodiscard]] bool observe_datagram(std::span<const std::byte> bytes,
                                      std::uint64_t arrived_at_us);
  [[nodiscard]] std::optional<BenchmarkCollectionResult>
  complete(std::uint64_t completed_at_us,
           std::span<const BenchmarkRttObservation> rtt_observations);
  void cancel() noexcept;

 private:
  struct DatagramObservation {
    std::uint64_t sent_at_us{};
    std::uint64_t arrived_at_us{};
    std::uint32_t reorder_distance{};
  };

  BenchmarkCollectorPlan plan_{};
  std::vector<bool> reliable_received_;
  std::vector<std::optional<DatagramObservation>> datagrams_;
  std::vector<std::uint64_t> arrival_order_;
  std::optional<std::uint64_t> first_reliable_arrival_us_;
  std::uint64_t reliable_bytes_{};
  std::uint64_t highest_arrival_sequence_{};
  bool has_arrival_sequence_{};
  bool active_{};
  bool canceled_{};
};

}  // namespace beacon::android::streamcore
