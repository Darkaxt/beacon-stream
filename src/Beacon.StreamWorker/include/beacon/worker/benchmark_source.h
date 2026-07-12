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

class BenchmarkSource {
 public:
  [[nodiscard]] bool start(BenchmarkSourcePlan plan);
  [[nodiscard]] std::optional<BenchmarkReliablePacket>
  next_reliable(std::uint64_t sent_at_us);
  [[nodiscard]] std::optional<BenchmarkDatagramPacket>
  next_datagram(std::uint64_t sent_at_us);
  void cancel() noexcept;

  [[nodiscard]] bool complete() const noexcept;
  [[nodiscard]] bool canceled() const noexcept;

 private:
  BenchmarkSourcePlan plan_{};
  std::uint64_t next_reliable_sequence_{};
  std::uint64_t next_datagram_sequence_{};
  bool active_{};
  bool canceled_{};
};

}  // namespace beacon::worker
