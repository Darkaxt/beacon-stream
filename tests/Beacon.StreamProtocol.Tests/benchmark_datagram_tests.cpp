#include "beacon/stream/benchmark_datagram.h"

#include "test_failure.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

using beacon::stream::BenchmarkDatagramHeader;

void benchmark_datagram_round_trips_exact_measurement_fields() {
  BenchmarkDatagramHeader expected{
      .run_token = {std::byte{0x10}, std::byte{0x11}, std::byte{0x12},
                    std::byte{0x13}, std::byte{0x14}, std::byte{0x15},
                    std::byte{0x16}, std::byte{0x17}, std::byte{0x18},
                    std::byte{0x19}, std::byte{0x1a}, std::byte{0x1b},
                    std::byte{0x1c}, std::byte{0x1d}, std::byte{0x1e},
                    std::byte{0x1f}},
      .round_id = 7,
      .sequence = 42,
      .sent_at_us = 987'654,
      .payload_bytes = 1000,
  };
  std::vector<std::byte> bytes(
      beacon::stream::benchmark_datagram_header_bytes + expected.payload_bytes,
      std::byte{0x5a});

  BEACON_TEST_REQUIRE(beacon::stream::serialize_benchmark_datagram_header(
      expected,
      std::span<std::byte, beacon::stream::benchmark_datagram_header_bytes>{
          bytes.data(), beacon::stream::benchmark_datagram_header_bytes}));
  auto parsed = beacon::stream::parse_benchmark_datagram(bytes);

  BEACON_TEST_REQUIRE(parsed.has_value());
  BEACON_TEST_REQUIRE(parsed->header == expected);
  BEACON_TEST_REQUIRE(parsed->payload.size() == expected.payload_bytes);
  BEACON_TEST_REQUIRE(parsed->payload.front() == std::byte{0x5a});
}

void malformed_or_non_benchmark_datagrams_are_rejected() {
  std::array<std::byte, beacon::stream::benchmark_datagram_header_bytes>
      bytes{};
  BEACON_TEST_REQUIRE(!beacon::stream::parse_benchmark_datagram(bytes).has_value());

  BenchmarkDatagramHeader header{
      .run_token = {},
      .round_id = 1,
      .sequence = 1,
      .sent_at_us = 1,
      .payload_bytes = 20,
  };
  BEACON_TEST_REQUIRE(beacon::stream::serialize_benchmark_datagram_header(
      header, bytes));
  BEACON_TEST_REQUIRE(!beacon::stream::parse_benchmark_datagram(bytes).has_value());

  std::vector<std::byte> truncated(bytes.begin(), bytes.end());
  truncated.resize(truncated.size() + 19, std::byte{0x22});
  BEACON_TEST_REQUIRE(
      !beacon::stream::parse_benchmark_datagram(truncated).has_value());
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    benchmark_datagram_round_trips_exact_measurement_fields();
    malformed_or_non_benchmark_datagrams_are_rejected();
  });
}
