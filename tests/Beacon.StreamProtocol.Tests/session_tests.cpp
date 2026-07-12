#include "beacon/stream/session.h"

#include "fake_transport.h"

#include <algorithm>
#include "test_failure.h"
#include <string>

namespace {

using beacon::stream::SessionEventKind;
using beacon::stream::SessionHandshake;
using beacon::stream::SessionIdentity;
using beacon::stream::SessionResult;
using beacon::stream::SessionState;
using beacon::stream::StreamSession;
using beacon::stream::TicketGrant;
using beacon::stream::testing::FakeTransport;

const SessionIdentity identity{"client-a", "session-a", 7};

TicketGrant grant(std::string token) {
  return {
      .token = std::move(token),
      .client_id = "client-a",
      .session_id = "session-a",
      .plan_revision = 7,
  };
}

SessionHandshake handshake(std::string token) {
  return {
      .token = std::move(token),
      .client_id = "client-a",
      .session_id = "session-a",
      .plan_revision = 7,
  };
}

void tickets_are_bound_and_accepted_once() {
  FakeTransport transport;
  StreamSession session(identity, transport);
  BEACON_TEST_REQUIRE(session.authorize_ticket(grant("ticket-a")) == SessionResult::accepted);

  auto wrong_client = handshake("ticket-a");
  wrong_client.client_id = "client-b";
  BEACON_TEST_REQUIRE(session.accept(wrong_client) == SessionResult::client_mismatch);

  auto wrong_session = handshake("ticket-a");
  wrong_session.session_id = "session-b";
  BEACON_TEST_REQUIRE(session.accept(wrong_session) == SessionResult::session_mismatch);

  auto wrong_plan = handshake("ticket-a");
  wrong_plan.plan_revision = 8;
  BEACON_TEST_REQUIRE(session.accept(wrong_plan) == SessionResult::plan_mismatch);
  BEACON_TEST_REQUIRE(transport.open_count() == 0);

  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-a")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.state() == SessionState::connected);
  BEACON_TEST_REQUIRE(transport.open_count() == 1);

  BEACON_TEST_REQUIRE(session.connection_lost() == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.state() == SessionState::disconnected);
  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-a")) == SessionResult::ticket_replayed);
}

void reconnect_requires_a_new_ticket_and_preserves_session_identity() {
  FakeTransport transport;
  StreamSession session(identity, transport);
  BEACON_TEST_REQUIRE(session.authorize_ticket(grant("ticket-a")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-a")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.connection_lost() == SessionResult::accepted);

  BEACON_TEST_REQUIRE(session.authorize_ticket(grant("ticket-b")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-b")) == SessionResult::accepted);

  BEACON_TEST_REQUIRE(session.state() == SessionState::connected);
  BEACON_TEST_REQUIRE(session.metrics().accepted_connections == 2);
  BEACON_TEST_REQUIRE(session.metrics().connection_losses == 1);
  BEACON_TEST_REQUIRE(transport.open_count() == 2);
  BEACON_TEST_REQUIRE(transport.close_count() == 1);
}

void stop_and_shutdown_release_each_resource_once() {
  FakeTransport transport;
  StreamSession session(identity, transport);
  BEACON_TEST_REQUIRE(session.authorize_ticket(grant("ticket-a")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-a")) == SessionResult::accepted);

  BEACON_TEST_REQUIRE(session.stop() == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.stop() == SessionResult::already_stopped);
  BEACON_TEST_REQUIRE(session.state() == SessionState::stopped);
  BEACON_TEST_REQUIRE(transport.close_count() == 1);

  BEACON_TEST_REQUIRE(session.shutdown() == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.shutdown() == SessionResult::already_shutdown);
  BEACON_TEST_REQUIRE(session.state() == SessionState::shutdown);
  BEACON_TEST_REQUIRE(transport.shutdown_count() == 1);

  const auto events = session.take_events();
  BEACON_TEST_REQUIRE(std::ranges::count(events, SessionEventKind::stopped, &beacon::stream::SessionEvent::kind) == 1);
  BEACON_TEST_REQUIRE(std::ranges::count(events, SessionEventKind::shutdown, &beacon::stream::SessionEvent::kind) == 1);
}

void shutdown_from_connected_closes_and_releases_once() {
  FakeTransport transport;
  StreamSession session(identity, transport);
  BEACON_TEST_REQUIRE(session.authorize_ticket(grant("ticket-a")) == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.accept(handshake("ticket-a")) == SessionResult::accepted);

  BEACON_TEST_REQUIRE(session.shutdown() == SessionResult::accepted);
  BEACON_TEST_REQUIRE(session.shutdown() == SessionResult::already_shutdown);

  BEACON_TEST_REQUIRE(transport.close_count() == 1);
  BEACON_TEST_REQUIRE(transport.shutdown_count() == 1);
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    tickets_are_bound_and_accepted_once();
    reconnect_requires_a_new_ticket_and_preserves_session_identity();
    stop_and_shutdown_release_each_resource_once();
    shutdown_from_connected_closes_and_releases_once();
  });
}
