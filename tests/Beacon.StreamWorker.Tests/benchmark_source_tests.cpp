#include "beacon/worker/benchmark_source.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <array>
#include <cstddef>

namespace {

void source_emits_exact_counts_sequences_and_bytes() {
  beacon::worker::BenchmarkSource source;
  const std::array token{
      std::byte{0x00}, std::byte{0x01}, std::byte{0x02}, std::byte{0x03},
      std::byte{0x04}, std::byte{0x05}, std::byte{0x06}, std::byte{0x07},
      std::byte{0x08}, std::byte{0x09}, std::byte{0x0a}, std::byte{0x0b},
      std::byte{0x0c}, std::byte{0x0d}, std::byte{0x0e}, std::byte{0x0f}};
  BEACON_TEST_REQUIRE(source.start({.run_token = token,
                                    .reliable_packet_count = 2,
                                    .reliable_payload_bytes = 32,
                                    .datagram_packet_count = 3,
                                    .datagram_payload_bytes = 48}));

  auto reliable_1 = source.next_reliable(100);
  auto reliable_2 = source.next_reliable(110);
  BEACON_TEST_REQUIRE(reliable_1.has_value());
  BEACON_TEST_REQUIRE(reliable_2.has_value());
  BEACON_TEST_REQUIRE(reliable_1->sequence == 0);
  BEACON_TEST_REQUIRE(reliable_2->sequence == 1);
  BEACON_TEST_REQUIRE(reliable_1->sent_at_us == 100);
  BEACON_TEST_REQUIRE(reliable_1->payload.size() == 32);
  BEACON_TEST_REQUIRE(!source.next_reliable(120).has_value());

  auto datagram_1 = source.next_datagram(200);
  auto datagram_2 = source.next_datagram(210);
  auto datagram_3 = source.next_datagram(220);
  BEACON_TEST_REQUIRE(datagram_1.has_value());
  BEACON_TEST_REQUIRE(datagram_2.has_value());
  BEACON_TEST_REQUIRE(datagram_3.has_value());
  BEACON_TEST_REQUIRE(datagram_1->sequence == 0);
  BEACON_TEST_REQUIRE(datagram_3->sequence == 2);
  BEACON_TEST_REQUIRE(datagram_1->bytes.size() ==
                      beacon::stream::benchmark_datagram_header_bytes + 48);
  BEACON_TEST_REQUIRE(!source.next_datagram(230).has_value());
  BEACON_TEST_REQUIRE(source.complete());
}

void cancellation_ends_generation_without_fabricating_completion() {
  beacon::worker::BenchmarkSource source;
  BEACON_TEST_REQUIRE(source.start({.run_token = {},
                                    .reliable_packet_count = 1,
                                    .reliable_payload_bytes = 16,
                                    .datagram_packet_count = 1,
                                    .datagram_payload_bytes = 16}));
  source.cancel();

  BEACON_TEST_REQUIRE(source.canceled());
  BEACON_TEST_REQUIRE(!source.complete());
  BEACON_TEST_REQUIRE(!source.next_reliable(1).has_value());
  BEACON_TEST_REQUIRE(!source.next_datagram(1).has_value());
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    source_emits_exact_counts_sequences_and_bytes();
    cancellation_ends_generation_without_fabricating_completion();
  });
}
