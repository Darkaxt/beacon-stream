#include "../benchmark_collector.h"

#include "test_failure.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <vector>

namespace {

using beacon::android::streamcore::BenchmarkCollector;
using beacon::android::streamcore::BenchmarkCollectorPlan;
using beacon::android::streamcore::BenchmarkRttObservation;

std::array<std::byte, 16> token(std::byte seed) {
  std::array<std::byte, 16> value{};
  for (std::size_t index = 0; index < value.size(); ++index) {
    value[index] = static_cast<std::byte>(std::to_integer<unsigned>(seed) + index);
  }
  return value;
}

std::vector<std::byte> datagram(const std::array<std::byte, 16> &run_token,
                                std::uint64_t sequence,
                                std::uint64_t sent_at_us,
                                std::uint32_t payload_bytes) {
  std::vector<std::byte> bytes(
      beacon::stream::benchmark_datagram_header_bytes + payload_bytes,
      std::byte{0x33});
  const beacon::stream::BenchmarkDatagramHeader header{
      .run_token = run_token,
      .round_id = 2,
      .sequence = sequence,
      .sent_at_us = sent_at_us,
      .payload_bytes = payload_bytes};
  BEACON_TEST_REQUIRE(beacon::stream::serialize_benchmark_datagram_header(
      header,
      std::span<std::byte, beacon::stream::benchmark_datagram_header_bytes>{
          bytes.data(), beacon::stream::benchmark_datagram_header_bytes}));
  return bytes;
}

void completion_produces_measured_throughput_loss_reorder_jitter_and_rtt() {
  BenchmarkCollector collector;
  const auto run_token = token(std::byte{0x10});
  BEACON_TEST_REQUIRE(collector.start({.run_token = run_token,
                                      .reliable_packet_count = 2,
                                      .reliable_payload_bytes = 1000,
                                      .datagram_packet_count = 4,
                                      .datagram_payload_bytes = 100}));

  BEACON_TEST_REQUIRE(collector.observe_reliable(0, 1000, 1'000));
  BEACON_TEST_REQUIRE(collector.observe_reliable(1, 1000, 2'000));
  BEACON_TEST_REQUIRE(
      collector.observe_datagram(datagram(run_token, 0, 10, 100), 100));
  BEACON_TEST_REQUIRE(
      collector.observe_datagram(datagram(run_token, 2, 30, 100), 300));
  BEACON_TEST_REQUIRE(
      collector.observe_datagram(datagram(run_token, 1, 20, 100), 400));

  const std::array rtt{
      BenchmarkRttObservation{.sequence = 0, .rtt_us = 2'000},
      BenchmarkRttObservation{.sequence = 1, .rtt_us = 2'500},
      BenchmarkRttObservation{.sequence = 2, .rtt_us = 3'000}};
  auto result = collector.complete(3'000, rtt);

  BEACON_TEST_REQUIRE(result.has_value());
  BEACON_TEST_REQUIRE(result->sustainable_throughput_mbps == 8.0);
  BEACON_TEST_REQUIRE(result->samples.size() == 4);
  BEACON_TEST_REQUIRE(result->samples[0].received);
  BEACON_TEST_REQUIRE(result->samples[0].rtt_us == 2'000);
  BEACON_TEST_REQUIRE(result->samples[1].reorder_distance == 1);
  BEACON_TEST_REQUIRE(result->samples[3].received == false);
  BEACON_TEST_REQUIRE(result->samples[3].rtt_us == 0);
  BEACON_TEST_REQUIRE(result->received_datagrams == 3);
}

void cancellation_and_run_replacement_never_fabricate_completion() {
  BenchmarkCollector collector;
  const auto first = token(std::byte{0x20});
  const auto second = token(std::byte{0x40});
  BEACON_TEST_REQUIRE(collector.start({.run_token = first,
                                      .reliable_packet_count = 1,
                                      .reliable_payload_bytes = 10,
                                      .datagram_packet_count = 1,
                                      .datagram_payload_bytes = 10}));
  collector.cancel();
  BEACON_TEST_REQUIRE(!collector.complete(100, {}).has_value());

  BEACON_TEST_REQUIRE(collector.start({.run_token = second,
                                      .reliable_packet_count = 1,
                                      .reliable_payload_bytes = 10,
                                      .datagram_packet_count = 1,
                                      .datagram_payload_bytes = 10}));
  BEACON_TEST_REQUIRE(
      !collector.observe_datagram(datagram(first, 0, 1, 10), 2));
  BEACON_TEST_REQUIRE(
      collector.observe_datagram(datagram(second, 0, 1, 10), 2));
}

void received_datagrams_require_correlated_rtt_evidence() {
  BenchmarkCollector collector;
  const auto run_token = token(std::byte{0x60});
  BEACON_TEST_REQUIRE(collector.start({.run_token = run_token,
                                      .reliable_packet_count = 1,
                                      .reliable_payload_bytes = 10,
                                      .datagram_packet_count = 1,
                                      .datagram_payload_bytes = 10}));
  BEACON_TEST_REQUIRE(collector.observe_reliable(0, 10, 10));
  BEACON_TEST_REQUIRE(
      collector.observe_datagram(datagram(run_token, 0, 1, 10), 11));
  BEACON_TEST_REQUIRE(!collector.complete(20, {}).has_value());
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    completion_produces_measured_throughput_loss_reorder_jitter_and_rtt();
    cancellation_and_run_replacement_never_fabricate_completion();
    received_datagrams_require_correlated_rtt_evidence();
  });
}
