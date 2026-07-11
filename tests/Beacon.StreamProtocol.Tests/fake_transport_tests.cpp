#include "fake_transport.h"

#include "test_failure.h"
#include <cstddef>
#include <cstdint>
#include <vector>

namespace {

using beacon::stream::StreamChannel;
using beacon::stream::TransportPacket;
using beacon::stream::TransportSendResult;
using beacon::stream::testing::FakeTransport;
using beacon::stream::testing::FaultAction;

TransportPacket packet(std::uint64_t sequence) {
  return {
      .channel = StreamChannel::media,
      .sequence = sequence,
      .payload = {static_cast<std::byte>(sequence)},
  };
}

void faults_are_selected_only_by_packet_sequence() {
  FakeTransport transport;
  BEACON_TEST_REQUIRE(transport.open_connection());
  transport.set_fault(1, FaultAction::hold);
  transport.set_fault(3, FaultAction::duplicate);
  transport.set_fault(4, FaultAction::drop);

  BEACON_TEST_REQUIRE(transport.send(packet(1)) == TransportSendResult::accepted);
  BEACON_TEST_REQUIRE(transport.send(packet(2)) == TransportSendResult::accepted);
  BEACON_TEST_REQUIRE(transport.delivered_packets().size() == 1);
  BEACON_TEST_REQUIRE(transport.delivered_packets()[0].sequence == 2);

  transport.release_held();
  BEACON_TEST_REQUIRE(transport.delivered_packets().size() == 2);
  BEACON_TEST_REQUIRE(transport.delivered_packets()[1].sequence == 1);

  BEACON_TEST_REQUIRE(transport.send(packet(3)) == TransportSendResult::accepted);
  BEACON_TEST_REQUIRE(transport.send(packet(4)) == TransportSendResult::accepted);
  BEACON_TEST_REQUIRE(transport.delivered_packets().size() == 4);
  BEACON_TEST_REQUIRE(transport.delivered_packets()[2].sequence == 3);
  BEACON_TEST_REQUIRE(transport.delivered_packets()[3].sequence == 3);
}

void shutdown_is_idempotent_and_closes_the_connection_once() {
  FakeTransport transport;
  BEACON_TEST_REQUIRE(transport.open_connection());

  transport.shutdown();
  transport.shutdown();

  BEACON_TEST_REQUIRE(transport.close_count() == 1);
  BEACON_TEST_REQUIRE(transport.shutdown_count() == 1);
  BEACON_TEST_REQUIRE(transport.send(packet(5)) == TransportSendResult::connection_closed);
}

}  // namespace

int main() {
  faults_are_selected_only_by_packet_sequence();
  shutdown_is_idempotent_and_closes_the_connection_once();
  return 0;
}
