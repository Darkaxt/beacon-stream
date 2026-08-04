#pragma once

#include <array>
#include <cstddef>
#include <cstdint>
#include <optional>
#include <span>

namespace beacon::stream {

inline constexpr std::uint8_t benchmark_datagram_version = 1;
inline constexpr std::size_t benchmark_datagram_header_bytes = 45;

struct BenchmarkDatagramHeader {
  std::array<std::byte, 16> run_token{};
  std::uint32_t round_id{};
  std::uint64_t sequence{};
  std::uint64_t sent_at_us{};
  std::uint32_t payload_bytes{};

  bool operator==(const BenchmarkDatagramHeader &) const = default;
};

struct ParsedBenchmarkDatagram {
  BenchmarkDatagramHeader header;
  std::span<const std::byte> payload;
};

[[nodiscard]] bool serialize_benchmark_datagram_header(
    const BenchmarkDatagramHeader &header,
    std::span<std::byte, benchmark_datagram_header_bytes> output) noexcept;

[[nodiscard]] std::optional<ParsedBenchmarkDatagram>
parse_benchmark_datagram(std::span<const std::byte> bytes) noexcept;

}  // namespace beacon::stream
