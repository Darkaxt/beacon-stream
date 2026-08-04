#pragma once

#include "beacon/stream/benchmark_datagram.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <vector>

namespace beacon::worker {

struct BenchmarkSourcePlan {
  std::array<std::byte, 16> run_token{};
  std::uint32_t reliable_packet_count{};
  std::uint32_t reliable_payload_bytes{};
  std::uint32_t datagram_packet_count{};
  std::uint32_t datagram_payload_bytes{};
};

struct BenchmarkReliablePacket {
  std::uint64_t sequence{};
  std::uint64_t sent_at_us{};
  std::vector<std::byte> payload;
};

struct BenchmarkDatagramPacket {
  std::uint64_t sequence{};
  std::vector<std::byte> bytes;
};

enum class BenchmarkDatagramFinalState { acknowledged, lost, canceled };

struct BenchmarkRttResult {
  std::uint64_t sequence{};
  std::uint64_t rtt_us{};
};

class BenchmarkSource {
 public:
  [[nodiscard]] bool start(BenchmarkSourcePlan plan);
  [[nodiscard]] std::optional<BenchmarkReliablePacket>
  next_reliable(std::uint64_t sent_at_us);
  [[nodiscard]] std::optional<BenchmarkDatagramPacket>
  next_datagram(std::uint64_t sent_at_us);
  [[nodiscard]] bool record_datagram_final(
      std::uint64_t sequence, BenchmarkDatagramFinalState state,
      std::uint64_t rtt_us);
  void cancel() noexcept;

  [[nodiscard]] bool complete() const noexcept;
  [[nodiscard]] bool ready_to_complete() const noexcept;
  [[nodiscard]] bool canceled() const noexcept;
  [[nodiscard]] std::vector<BenchmarkRttResult> rtt_observations() const;

 private:
  BenchmarkSourcePlan plan_{};
  std::uint64_t next_reliable_sequence_{};
  std::uint64_t next_datagram_sequence_{};
  struct DatagramFinalResult {
    BenchmarkDatagramFinalState state;
    std::uint64_t rtt_us{};
  };
  std::vector<std::optional<DatagramFinalResult>> datagram_final_results_;
  bool active_{};
  bool canceled_{};
};

}  // namespace beacon::worker
