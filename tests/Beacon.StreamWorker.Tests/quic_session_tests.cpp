#include "beacon/worker/quic_listener.h"
#include "beacon/worker/quic_session_protocol.h"
#include "beacon/worker/secure_bytes.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "stream_control.pb.h"

#include <array>
#include <cstddef>
#include <cstdint>
#include <span>
#include <string_view>
#include <vector>

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

stream_v1::SessionStreamEnvelope start_session(std::uint64_t sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(sequence);
  auto *video = message.mutable_start_session()->mutable_selected_video();
  video->set_codec(stream_v1::VIDEO_CODEC_H264);
  video->set_width(2560);
  video->set_height(1600);
  video->set_frames_per_second_numerator(120);
  video->set_frames_per_second_denominator(1);
  video->set_dynamic_range(stream_v1::DYNAMIC_RANGE_SDR);
  return message;
}

stream_v1::SessionStreamEnvelope start_benchmark(std::uint64_t sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(sequence);
  auto *benchmark = message.mutable_start_benchmark();
  benchmark->set_run_id("11111111-1111-1111-1111-111111111111");
  benchmark->set_schema_version(3);
  auto *reliable = benchmark->mutable_reliable_round();
  reliable->set_packet_count(4);
  reliable->set_payload_bytes(1024);
  reliable->set_measurement_interval_us(500'000);
  auto *datagram = benchmark->mutable_datagram_round();
  datagram->set_packet_count(8);
  datagram->set_payload_bytes(1000);
  datagram->set_measurement_interval_us(500'000);
  return message;
}

stream_v1::SessionStreamEnvelope cancel_benchmark(std::uint64_t sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(sequence);
  message.mutable_cancel_benchmark()->set_run_id(
      "11111111-1111-1111-1111-111111111111");
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
  BEACON_TEST_REQUIRE(second.accepted_authentication.has_value());
  BEACON_TEST_REQUIRE(second.accepted_authentication->session_id ==
                      "session-a");
  BEACON_TEST_REQUIRE(second.accepted_authentication->session_generation == 1);
  BEACON_TEST_REQUIRE(
      second.accepted_authentication->maximum_datagram_bytes == 1232);
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
  BEACON_TEST_REQUIRE(input_output.inputs.size() == 1);
  BEACON_TEST_REQUIRE(input_output.inputs[0].session_generation == 1);
  BEACON_TEST_REQUIRE(
      input_output.inputs[0].input.SerializeAsString() ==
      input.SerializeAsString());
  BEACON_TEST_REQUIRE(feedback_output.feedback.size() == 1);
  BEACON_TEST_REQUIRE(feedback_output.feedback[0].session_generation == 1);
  BEACON_TEST_REQUIRE(
      feedback_output.feedback[0].feedback.SerializeAsString() ==
      feedback.SerializeAsString());
}

void start_session_is_typed_once_per_authenticated_generation() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-start")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);

  const auto auth = protocol.receive(
      QuicPeerStreamRole::session, frame(authenticate("raw-ticket-start")),
      1'000);
  const auto first = protocol.receive(QuicPeerStreamRole::session,
                                      frame(start_session(2)), 1'000);
  const auto duplicate = protocol.receive(QuicPeerStreamRole::session,
                                          frame(start_session(3)), 1'000);

  BEACON_TEST_REQUIRE(auth.accepted_authentication.has_value());
  BEACON_TEST_REQUIRE(first.accepted_start_session.has_value());
  BEACON_TEST_REQUIRE(
      first.accepted_start_session->session_generation == 1);
  BEACON_TEST_REQUIRE(
      first.accepted_start_session->maximum_datagram_bytes == 1232);
  BEACON_TEST_REQUIRE(
      first.accepted_start_session->start_session.SerializeAsString() ==
      start_session(2).start_session().SerializeAsString());
  BEACON_TEST_REQUIRE(!duplicate.accepted_start_session.has_value());
}

void benchmark_start_and_cancel_are_typed_for_the_authenticated_generation() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("benchmark-ticket")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1200);

  auto authenticated = protocol.receive(
      QuicPeerStreamRole::session, frame(authenticate("benchmark-ticket")),
      1'000);
  BEACON_TEST_REQUIRE(authenticated.accepted_authentication.has_value());

  auto started = protocol.receive(QuicPeerStreamRole::session,
                                  frame(start_benchmark(2)), 1'001);
  BEACON_TEST_REQUIRE(!started.close_connection);
  BEACON_TEST_REQUIRE(started.accepted_start_benchmark.has_value());
  BEACON_TEST_REQUIRE(started.accepted_start_benchmark->session_generation ==
                      authenticated.accepted_authentication->session_generation);
  BEACON_TEST_REQUIRE(started.accepted_start_benchmark->start_benchmark.run_id() ==
                      "11111111-1111-1111-1111-111111111111");
  BEACON_TEST_REQUIRE(
      started.accepted_start_benchmark->start_benchmark.datagram_round()
          .packet_count() == 8);

  auto canceled = protocol.receive(QuicPeerStreamRole::session,
                                   frame(cancel_benchmark(3)), 1'002);
  BEACON_TEST_REQUIRE(!canceled.close_connection);
  BEACON_TEST_REQUIRE(canceled.accepted_cancel_benchmark.has_value());
  BEACON_TEST_REQUIRE(canceled.accepted_cancel_benchmark->session_generation ==
                      authenticated.accepted_authentication->session_generation);
  BEACON_TEST_REQUIRE(canceled.accepted_cancel_benchmark->cancel_benchmark.run_id() ==
                      "11111111-1111-1111-1111-111111111111");
}

void reset_and_fresh_authentication_allocate_a_new_generation() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-generation-a")));
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-generation-b")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);

  const auto first = protocol.receive(
      QuicPeerStreamRole::session,
      frame(authenticate("raw-ticket-generation-a")), 1'000);
  protocol.reset();
  protocol.set_maximum_datagram_bytes(1232);
  const auto second = protocol.receive(
      QuicPeerStreamRole::session,
      frame(authenticate("raw-ticket-generation-b")), 1'000);

  BEACON_TEST_REQUIRE(first.accepted_authentication->session_generation == 1);
  BEACON_TEST_REQUIRE(second.accepted_authentication->session_generation == 2);
}

void stale_old_connection_receive_does_not_touch_current_protocol_state() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-connection-a")));
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-connection-b")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);
  protocol.begin_connection(10);
  BEACON_TEST_REQUIRE(
      protocol
          .receive(10, QuicPeerStreamRole::session,
                   frame(authenticate("raw-ticket-connection-a")), 1'000)
          .accepted_authentication.has_value());

  protocol.begin_connection(11);
  protocol.set_maximum_datagram_bytes(1232);
  BEACON_TEST_REQUIRE(
      protocol
          .receive(11, QuicPeerStreamRole::session,
                   frame(authenticate("raw-ticket-connection-b")), 1'000)
          .accepted_authentication.has_value());

  stream_v1::InputStreamEnvelope stale_input;
  stale_input.set_protocol_version(1);
  stale_input.set_session_id("session-a");
  stale_input.set_sequence(99);
  stale_input.mutable_input_batch()->add_events()->mutable_keyboard()->set_scan_code(
      31);
  const auto stale = protocol.receive(10, QuicPeerStreamRole::input,
                                      frame(stale_input), 1'000);

  stream_v1::InputStreamEnvelope current_input = stale_input;
  current_input.set_sequence(1);
  const auto current = protocol.receive(11, QuicPeerStreamRole::input,
                                        frame(current_input), 1'000);
  BEACON_TEST_REQUIRE(stale.stale_callback);
  BEACON_TEST_REQUIRE(stale.inputs.empty());
  BEACON_TEST_REQUIRE(protocol.authenticated());
  BEACON_TEST_REQUIRE(current.inputs.size() == 1);
  BEACON_TEST_REQUIRE(current.inputs[0].input.sequence() == 1);
}

void secure_clear_observes_zeroes_before_pending_bytes_are_released() {
  std::vector<std::byte> pending{std::byte{0x01}, std::byte{0x7f},
                                 std::byte{0xff}};
  bool observed = false;
  const auto observer = [](std::span<const std::byte> bytes,
                           void *context) noexcept {
    auto &was_observed = *static_cast<bool *>(context);
    was_observed = !bytes.empty() &&
                   std::ranges::all_of(bytes, [](std::byte value) {
                     return value == std::byte{};
                   });
  };

  beacon::worker::secure_clear_bytes(pending, observer, &observed);

  BEACON_TEST_REQUIRE(observed);
  BEACON_TEST_REQUIRE(pending.empty());
}

struct ProtocolWipeObservation {
  std::size_t nonempty_wipes{};
  bool all_zero{true};
};

void observe_protocol_wipe(std::span<const std::byte> bytes,
                           void *context) noexcept {
  auto &observation = *static_cast<ProtocolWipeObservation *>(context);
  if (bytes.empty()) {
    return;
  }
  ++observation.nonempty_wipes;
  observation.all_zero =
      observation.all_zero &&
      std::ranges::all_of(bytes,
                          [](std::byte value) { return value == std::byte{}; });
}

void oversized_partial_authentication_is_wiped_before_buffer_reuse() {
  AuthorizedQuicTicketStore store;
  ProtocolWipeObservation observation;
  QuicSessionProtocol protocol(store, observe_protocol_wipe, &observation);
  const std::array partial_auth{std::byte{0x01}, std::byte{0x7f},
                                std::byte{0x55}};
  BEACON_TEST_REQUIRE(
      !protocol.receive(QuicPeerStreamRole::session, partial_auth, 1'000)
           .close_connection);

  const std::vector oversized(
      static_cast<std::size_t>(
          beacon::worker::maximum_stream_message_bytes) +
          5U,
      std::byte{0x33});
  BEACON_TEST_REQUIRE(
      protocol.receive(QuicPeerStreamRole::session, oversized, 1'000)
          .close_connection);
  BEACON_TEST_REQUIRE(observation.nonempty_wipes == 1);
  BEACON_TEST_REQUIRE(observation.all_zero);
}

void malformed_authentication_is_wiped_before_logical_clear() {
  AuthorizedQuicTicketStore store;
  ProtocolWipeObservation observation;
  QuicSessionProtocol protocol(store, observe_protocol_wipe, &observation);
  const std::array malformed_length{std::byte{0x00}, std::byte{0x00},
                                    std::byte{0x00}, std::byte{0x00}};
  BEACON_TEST_REQUIRE(
      protocol.receive(QuicPeerStreamRole::session, malformed_length, 1'000)
          .close_connection);

  const std::array malformed_frame{std::byte{0x00}, std::byte{0x00},
                                   std::byte{0x00}, std::byte{0x01},
                                   std::byte{0xff}};
  BEACON_TEST_REQUIRE(
      protocol.receive(QuicPeerStreamRole::session, malformed_frame, 1'000)
          .close_connection);
  BEACON_TEST_REQUIRE(observation.nonempty_wipes == 2);
  BEACON_TEST_REQUIRE(observation.all_zero);
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
  return beacon::stream::testing::run_tests([] {
    ticket_is_consumed_once_without_retaining_the_raw_secret();
    ticket_identity_and_security_expiry_are_validated_before_consumption();
    revocation_and_duplicate_authorization_are_deterministic();
    stream_ids_have_one_unambiguous_role();
    fragmented_authentication_consumes_ticket_and_returns_negotiated_limit();
    replay_and_version_mismatch_return_typed_rejections();
    authenticated_streams_are_routed_independently();
    start_session_is_typed_once_per_authenticated_generation();
    benchmark_start_and_cancel_are_typed_for_the_authenticated_generation();
    reset_and_fresh_authentication_allocate_a_new_generation();
    stale_old_connection_receive_does_not_touch_current_protocol_state();
    secure_clear_observes_zeroes_before_pending_bytes_are_released();
    oversized_partial_authentication_is_wiped_before_buffer_reuse();
    malformed_authentication_is_wiped_before_logical_clear();
    unauthenticated_data_and_oversized_frames_fail_closed();
  });
}
