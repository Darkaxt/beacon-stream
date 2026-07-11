#include "beacon/worker/quic_listener.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>

namespace {

using beacon::worker::AuthorizedQuicTicket;
using beacon::worker::AuthorizedQuicTicketStore;
using beacon::worker::QuicTicketConsumeResult;

std::span<const std::byte> bytes(std::string_view value) {
  return {reinterpret_cast<const std::byte*>(value.data()), value.size()};
}

AuthorizedQuicTicket grant(std::string_view raw_ticket) {
  return {
      .hash = beacon::worker::hash_stream_ticket(bytes(raw_ticket)),
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = 2'000,
  };
}

void ticket_is_consumed_once_without_retaining_the_raw_secret() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-a")));

  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::accepted);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::replayed);
  BEACON_TEST_REQUIRE(store.consume(bytes("another-ticket"), "z-fold-7", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::unknown);
}

void ticket_identity_and_security_expiry_are_validated_before_consumption() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-b")));

  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "wrong-client", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::client_mismatch);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7", "wrong-session", 8, 1'000) ==
                      QuicTicketConsumeResult::session_mismatch);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7", "session-a", 9, 1'000) ==
                      QuicTicketConsumeResult::plan_mismatch);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7", "session-a", 8, 2'001) ==
                      QuicTicketConsumeResult::expired);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::accepted);
}

void revocation_and_duplicate_authorization_are_deterministic() {
  AuthorizedQuicTicketStore store;
  auto ticket = grant("raw-ticket-c");
  BEACON_TEST_REQUIRE(store.authorize(ticket));
  BEACON_TEST_REQUIRE(!store.authorize(ticket));
  BEACON_TEST_REQUIRE(store.size() == 1);

  store.revoke(ticket.hash);
  BEACON_TEST_REQUIRE(store.size() == 0);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-c"), "z-fold-7", "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::unknown);
}

}  // namespace

int main() {
  ticket_is_consumed_once_without_retaining_the_raw_secret();
  ticket_identity_and_security_expiry_are_validated_before_consumption();
  revocation_and_duplicate_authorization_are_deterministic();
  return 0;
}
