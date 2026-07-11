#include "beacon/stream/msquic_transport.h"

#include "test_failure.h"

#include <algorithm>

namespace {

using beacon::stream::MsQuicConnectionState;
using beacon::stream::MsQuicEventKind;
using beacon::stream::MsQuicTransportState;

void alpn_must_match_the_single_beacon_protocol() {
  MsQuicTransportState accepted;
  MsQuicTransportState rejected;

  BEACON_TEST_REQUIRE(accepted.connected("beacon-stream/1"));
  BEACON_TEST_REQUIRE(accepted.state() == MsQuicConnectionState::connected);
  BEACON_TEST_REQUIRE(!rejected.connected("another-protocol"));
  BEACON_TEST_REQUIRE(rejected.state() == MsQuicConnectionState::failed);
}

void datagram_negotiation_owns_the_packet_size_limit() {
  MsQuicTransportState state;
  BEACON_TEST_REQUIRE(state.connected("beacon-stream/1"));

  state.datagram_state_changed(true, 1232);
  BEACON_TEST_REQUIRE(state.datagram_send_enabled());
  BEACON_TEST_REQUIRE(state.maximum_datagram_bytes() == 1232);

  state.datagram_state_changed(false, 0);
  BEACON_TEST_REQUIRE(!state.datagram_send_enabled());
  BEACON_TEST_REQUIRE(state.maximum_datagram_bytes() == 0);
}

void final_datagram_states_preserve_loss_evidence() {
  MsQuicTransportState state;
  BEACON_TEST_REQUIRE(state.connected("beacon-stream/1"));

  state.datagram_send_state_changed(40, QUIC_DATAGRAM_SEND_SENT);
  state.datagram_send_state_changed(40, QUIC_DATAGRAM_SEND_ACKNOWLEDGED);
  state.datagram_send_state_changed(41, QUIC_DATAGRAM_SEND_LOST_DISCARDED);
  state.datagram_send_state_changed(42, QUIC_DATAGRAM_SEND_CANCELED);

  const auto events = state.take_events();
  BEACON_TEST_REQUIRE(std::ranges::count(events, MsQuicEventKind::datagram_sent,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
  BEACON_TEST_REQUIRE(std::ranges::count(events, MsQuicEventKind::datagram_acknowledged,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
  BEACON_TEST_REQUIRE(std::ranges::count(events, MsQuicEventKind::datagram_lost,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
  BEACON_TEST_REQUIRE(std::ranges::count(events, MsQuicEventKind::datagram_canceled,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
}

void peer_and_transport_close_are_distinct_facts() {
  MsQuicTransportState graceful;
  BEACON_TEST_REQUIRE(graceful.connected("beacon-stream/1"));
  graceful.peer_closed(7);
  graceful.closed();
  BEACON_TEST_REQUIRE(graceful.state() == MsQuicConnectionState::closed);

  MsQuicTransportState failed;
  BEACON_TEST_REQUIRE(failed.connected("beacon-stream/1"));
  failed.transport_failed(QUIC_STATUS_CONNECTION_TIMEOUT, 9);
  failed.closed();
  BEACON_TEST_REQUIRE(failed.state() == MsQuicConnectionState::failed);

  const auto graceful_events = graceful.take_events();
  const auto failed_events = failed.take_events();
  BEACON_TEST_REQUIRE(std::ranges::count(graceful_events, MsQuicEventKind::peer_closed,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
  BEACON_TEST_REQUIRE(std::ranges::count(failed_events, MsQuicEventKind::transport_failed,
                                        &beacon::stream::MsQuicTransportEvent::kind) == 1);
}

}  // namespace

int main() {
  alpn_must_match_the_single_beacon_protocol();
  datagram_negotiation_owns_the_packet_size_limit();
  final_datagram_states_preserve_loss_evidence();
  peer_and_transport_close_are_distinct_facts();
  return 0;
}
