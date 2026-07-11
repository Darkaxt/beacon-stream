#include "beacon/worker/quic_listener.h"
#include "beacon/worker/quic_session_protocol.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "stream_control.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>

namespace {

using beacon::worker::AuthorizedQuicTicket;
using beacon::worker::AuthorizedQuicTicketStore;
using beacon::worker::QuicPeerStreamRole;
using beacon::worker::QuicSessionProtocol;
using beacon::worker::QuicTicketConsumeResult;
namespace stream_v1 = beacon::stream::v1;

std::span<const std::byte> bytes(std::string_view value) {
  return {reinterpret_cast<const std::byte *>(value.data()), value.size()};
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

template <typename Message>
std::vector<std::byte> frame(const Message &message) {
  const auto size = message.ByteSizeLong();
  std::vector<std::byte> result(4 + size);
  result[0] = static_cast<std::byte>((size >> 24U) & 0xffU);
  result[1] = static_cast<std::byte>((size >> 16U) & 0xffU);
  result[2] = static_cast<std::byte>((size >> 8U) & 0xffU);
  result[3] = static_cast<std::byte>(size & 0xffU);
  BEACON_TEST_REQUIRE(
      message.SerializeToArray(result.data() + 4, static_cast<int>(size)));
  return result;
}

stream_v1::SessionStreamEnvelope authenticate(std::string_view ticket) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(1);
  auto *auth = message.mutable_authenticate_session();
  auth->set_stream_ticket(ticket);
  auth->set_client_id("z-fold-7");
  auth->set_plan_revision(8);
  return message;
}

stream_v1::SessionStreamEnvelope
reply_from(const beacon::worker::QuicSessionProtocolOutput &output) {
  BEACON_TEST_REQUIRE(output.session_replies.size() == 1);
  stream_v1::SessionStreamEnvelope reply;
  BEACON_TEST_REQUIRE(reply.ParseFromArray(
      output.session_replies[0].data() + 4,
      static_cast<int>(output.session_replies[0].size() - 4)));
  return reply;
}

void ticket_is_consumed_once_without_retaining_the_raw_secret() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-a")));

  BEACON_TEST_REQUIRE(
      store.consume(bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
      QuicTicketConsumeResult::accepted);
  BEACON_TEST_REQUIRE(
      store.consume(bytes("raw-ticket-a"), "z-fold-7", "session-a", 8, 1'000) ==
      QuicTicketConsumeResult::replayed);
  BEACON_TEST_REQUIRE(store.consume(bytes("another-ticket"), "z-fold-7",
                                    "session-a", 8,
                                    1'000) == QuicTicketConsumeResult::unknown);
}

void ticket_identity_and_security_expiry_are_validated_before_consumption() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-b")));

  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "wrong-client",
                                    "session-a", 8, 1'000) ==
                      QuicTicketConsumeResult::client_mismatch);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7",
                                    "wrong-session", 8, 1'000) ==
                      QuicTicketConsumeResult::session_mismatch);
  BEACON_TEST_REQUIRE(
      store.consume(bytes("raw-ticket-b"), "z-fold-7", "session-a", 9, 1'000) ==
      QuicTicketConsumeResult::plan_mismatch);
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-b"), "z-fold-7",
                                    "session-a", 8,
                                    2'001) == QuicTicketConsumeResult::expired);
  BEACON_TEST_REQUIRE(
      store.consume(bytes("raw-ticket-b"), "z-fold-7", "session-a", 8, 1'000) ==
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
  BEACON_TEST_REQUIRE(store.consume(bytes("raw-ticket-c"), "z-fold-7",
                                    "session-a", 8,
                                    1'000) == QuicTicketConsumeResult::unknown);
}

void stream_ids_have_one_unambiguous_role() {
  BEACON_TEST_REQUIRE(beacon::worker::classify_peer_stream(0) ==
                      QuicPeerStreamRole::session);
  BEACON_TEST_REQUIRE(beacon::worker::classify_peer_stream(2) ==
                      QuicPeerStreamRole::input);
  BEACON_TEST_REQUIRE(beacon::worker::classify_peer_stream(6) ==
                      QuicPeerStreamRole::feedback);
  BEACON_TEST_REQUIRE(beacon::worker::classify_peer_stream(4) ==
                      QuicPeerStreamRole::invalid);
  BEACON_TEST_REQUIRE(beacon::worker::classify_peer_stream(10) ==
                      QuicPeerStreamRole::invalid);
}

void fragmented_authentication_consumes_ticket_and_returns_negotiated_limit() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-live")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);
  const auto bytes = frame(authenticate("raw-ticket-live"));

  const auto first = protocol.receive(QuicPeerStreamRole::session,
                                      std::span{bytes}.first(3), 1'000);
  const auto second = protocol.receive(QuicPeerStreamRole::session,
                                       std::span{bytes}.subspan(3), 1'000);

  BEACON_TEST_REQUIRE(!first.close_connection);
  BEACON_TEST_REQUIRE(first.session_replies.empty());
  BEACON_TEST_REQUIRE(protocol.authenticated());
  const auto reply = reply_from(second);
  BEACON_TEST_REQUIRE(reply.session_authenticated().accepted());
  BEACON_TEST_REQUIRE(reply.session_authenticated().maximum_datagram_bytes() ==
                      1232);
}

void replay_and_version_mismatch_return_typed_rejections() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-replay")));
  QuicSessionProtocol first(store);
  const auto accepted =
      first.receive(QuicPeerStreamRole::session,
                    frame(authenticate("raw-ticket-replay")), 1'000);
  BEACON_TEST_REQUIRE(reply_from(accepted).session_authenticated().accepted());

  QuicSessionProtocol replay(store);
  const auto replayed =
      replay.receive(QuicPeerStreamRole::session,
                     frame(authenticate("raw-ticket-replay")), 1'000);
  BEACON_TEST_REQUIRE(replayed.close_connection);
  BEACON_TEST_REQUIRE(
      reply_from(replayed).session_authenticated().error_code() ==
      stream_v1::SESSION_ERROR_CODE_TICKET_REPLAYED);

  auto wrong_version = authenticate("irrelevant");
  wrong_version.set_protocol_version(2);
  QuicSessionProtocol version(store);
  const auto rejected =
      version.receive(QuicPeerStreamRole::session, frame(wrong_version), 1'000);
  BEACON_TEST_REQUIRE(rejected.close_connection);
  BEACON_TEST_REQUIRE(
      reply_from(rejected).session_authenticated().error_code() ==
      stream_v1::SESSION_ERROR_CODE_UNSUPPORTED_VERSION);
}

void authenticated_streams_are_routed_independently() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-routes")));
  QuicSessionProtocol protocol(store);
  const auto auth = frame(authenticate("raw-ticket-routes"));
  BEACON_TEST_REQUIRE(
      !protocol.receive(QuicPeerStreamRole::session, auth, 1'000)
           .close_connection);

  stream_v1::SessionStreamEnvelope session;
  session.set_protocol_version(1);
  session.set_session_id("session-a");
  session.set_sequence(2);
  session.mutable_request_idr()->set_reason(
      stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);
  stream_v1::InputStreamEnvelope input;
  input.set_protocol_version(1);
  input.set_session_id("session-a");
  input.set_sequence(7);
  input.mutable_input_batch()->add_events()->mutable_keyboard()->set_scan_code(
      30);
  stream_v1::FeedbackStreamEnvelope feedback;
  feedback.set_protocol_version(1);
  feedback.set_session_id("session-a");
  feedback.set_sequence(9);
  feedback.mutable_queue_depth()->set_queued_access_units(2);

  const auto session_output =
      protocol.receive(QuicPeerStreamRole::session, frame(session), 1'000);
  const auto input_output =
      protocol.receive(QuicPeerStreamRole::input, frame(input), 1'000);
  const auto feedback_output =
      protocol.receive(QuicPeerStreamRole::feedback, frame(feedback), 1'000);
  BEACON_TEST_REQUIRE(session_output.packets.size() == 1);
  BEACON_TEST_REQUIRE(session_output.packets[0].channel ==
                      beacon::stream::StreamChannel::session);
  BEACON_TEST_REQUIRE(input_output.packets.size() == 1);
  BEACON_TEST_REQUIRE(input_output.packets[0].channel ==
                      beacon::stream::StreamChannel::input);
  BEACON_TEST_REQUIRE(feedback_output.packets.size() == 1);
  BEACON_TEST_REQUIRE(feedback_output.packets[0].channel ==
                      beacon::stream::StreamChannel::feedback);
}

void unauthenticated_data_and_oversized_frames_fail_closed() {
  AuthorizedQuicTicketStore store;
  QuicSessionProtocol protocol(store);
  stream_v1::InputStreamEnvelope input;
  input.set_protocol_version(1);
  input.set_session_id("session-a");
  input.set_sequence(1);
  input.mutable_input_batch();
  BEACON_TEST_REQUIRE(
      protocol.receive(QuicPeerStreamRole::input, frame(input), 1'000)
          .close_connection);

  protocol.reset();
  const std::array<std::byte, 4> oversized{std::byte{0}, std::byte{0x10},
                                           std::byte{0}, std::byte{1}};
  BEACON_TEST_REQUIRE(
      protocol.receive(QuicPeerStreamRole::session, oversized, 1'000)
          .close_connection);
}

} // namespace

int main() {
  ticket_is_consumed_once_without_retaining_the_raw_secret();
  ticket_identity_and_security_expiry_are_validated_before_consumption();
  revocation_and_duplicate_authorization_are_deterministic();
  stream_ids_have_one_unambiguous_role();
  fragmented_authentication_consumes_ticket_and_returns_negotiated_limit();
  replay_and_version_mismatch_return_typed_rejections();
  authenticated_streams_are_routed_independently();
  unauthenticated_data_and_oversized_frames_fail_closed();
  return 0;
}
