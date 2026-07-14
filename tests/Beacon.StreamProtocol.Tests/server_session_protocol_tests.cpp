#include "beacon/stream/server_session_protocol.h"
#include "beacon/stream/secure_bytes.h"

#include "test_failure.h"
#include "stream_control.pb.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <functional>
#include <optional>
#include <span>
#include <string>
#include <string_view>
#include <utility>
#include <variant>
#include <vector>

namespace {

using beacon::stream::QuicPeerStreamRole;
using beacon::stream::ServerConnectionDisposition;
using QuicSessionProtocol = beacon::stream::ServerSessionProtocol;
using QuicSessionProtocolOutput = beacon::stream::ServerSessionProtocolOutput;
namespace stream_v1 = beacon::stream::v1;

using AcceptedCancelBenchmark =
    QuicSessionProtocolOutput::AcceptedCancelBenchmark;
using AcceptedIdrRequest = QuicSessionProtocolOutput::AcceptedIdrRequest;
using AcceptedStartBenchmark =
    QuicSessionProtocolOutput::AcceptedStartBenchmark;
using AcceptedStartSession = QuicSessionProtocolOutput::AcceptedStartSession;
using AcceptedStopSession = QuicSessionProtocolOutput::AcceptedStopSession;

template <typename Action>
const Action *accepted_action(const QuicSessionProtocolOutput &output) {
  for (const auto &action : output.accepted_session_actions) {
    if (const auto *accepted = std::get_if<Action>(&action)) {
      return accepted;
    }
  }
  return nullptr;
}

bool kept_open(const QuicSessionProtocolOutput &output) {
  return output.connection_disposition ==
         ServerConnectionDisposition::keep_open;
}

bool protocol_failed(const QuicSessionProtocolOutput &output) {
  return output.connection_disposition ==
         ServerConnectionDisposition::protocol_failure;
}

std::span<const std::byte> bytes(std::string_view value) {
  return {reinterpret_cast<const std::byte *>(value.data()), value.size()};
}

stream_v1::SelectedVideoMode selected_video() {
  stream_v1::SelectedVideoMode video;
  video.set_codec(stream_v1::VIDEO_CODEC_H264);
  video.set_width(2560);
  video.set_height(1600);
  video.set_frames_per_second_numerator(120);
  video.set_frames_per_second_denominator(1);
  video.set_dynamic_range(stream_v1::DYNAMIC_RANGE_SDR);
  return video;
}

struct TestAuthorizedTicket {
  std::string raw_ticket;
  std::string client_id;
  std::string session_id;
  std::uint64_t plan_revision{};
  std::uint64_t expires_at_unix_ms{};
  std::optional<stream_v1::SelectedVideoMode> selected_video;
  std::optional<stream_v1::StartBenchmark> benchmark_plan;
};

TestAuthorizedTicket grant(std::string_view raw_ticket) {
  TestAuthorizedTicket ticket{
      .raw_ticket = std::string{raw_ticket},
      .client_id = "z-fold-7",
      .session_id = "session-a",
      .plan_revision = 8,
      .expires_at_unix_ms = 2'000,
      .selected_video = selected_video(),
      .benchmark_plan = std::nullopt,
  };
  return ticket;
}

class RecordingAuthorizer final
    : public beacon::stream::IStreamTicketAuthorizer {
public:
  bool authorize(TestAuthorizedTicket ticket) {
    if (ticket.client_id.empty() || ticket.session_id.empty() ||
        ticket.plan_revision == 0 || ticket.expires_at_unix_ms == 0 ||
        ticket.selected_video.has_value() == ticket.benchmark_plan.has_value()) {
      return false;
    }
    const auto duplicate =
        std::ranges::find_if(records_, [&ticket](const Record &record) {
          return record.ticket.raw_ticket == ticket.raw_ticket;
        });
    if (duplicate != records_.end()) {
      return false;
    }
    records_.push_back({.ticket = std::move(ticket)});
    return true;
  }

  beacon::stream::StreamTicketAuthorization authorize(
      std::span<const std::byte> ticket, std::string_view client_id,
      std::string_view session_id, std::uint64_t plan_revision,
      std::uint64_t now_unix_ms) override {
    ++calls;
    const auto found = std::ranges::find_if(records_, [ticket](const Record &record) {
      return std::ranges::equal(ticket, bytes(record.ticket.raw_ticket));
    });
    if (found == records_.end()) {
      return {.result =
                  beacon::stream::StreamTicketAuthorizationResult::unknown,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    if (found->consumed) {
      return {.result =
                  beacon::stream::StreamTicketAuthorizationResult::replayed,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    if (found->ticket.client_id != client_id) {
      return {.result = beacon::stream::StreamTicketAuthorizationResult::
                            client_mismatch,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    if (found->ticket.session_id != session_id) {
      return {.result = beacon::stream::StreamTicketAuthorizationResult::
                            session_mismatch,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    if (found->ticket.plan_revision != plan_revision) {
      return {.result =
                  beacon::stream::StreamTicketAuthorizationResult::plan_mismatch,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    if (now_unix_ms > found->ticket.expires_at_unix_ms) {
      return {.result =
                  beacon::stream::StreamTicketAuthorizationResult::expired,
              .selected_video = std::nullopt,
              .benchmark_plan = std::nullopt};
    }
    found->consumed = true;
    return {.result = beacon::stream::StreamTicketAuthorizationResult::accepted,
            .selected_video = found->ticket.selected_video,
            .benchmark_plan = found->ticket.benchmark_plan};
  }

  std::size_t calls{};

private:
  struct Record {
    TestAuthorizedTicket ticket;
    bool consumed{};
  };

  std::vector<Record> records_;
};

using AuthorizedQuicTicketStore = RecordingAuthorizer;

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

stream_v1::SessionStreamEnvelope stop_session(std::uint64_t sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(sequence);
  message.mutable_stop_session()->set_reason(
      stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
  return message;
}

stream_v1::SessionStreamEnvelope request_idr(std::uint64_t sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-a");
  message.set_sequence(sequence);
  message.mutable_request_idr()->set_reason(
      stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);
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
  benchmark->set_run_token(
      "\x00\x01\x02\x03\x04\x05\x06\x07\x08\x09\x0a\x0b\x0c\x0d\x0e\x0f", 16);
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
reply_from(const QuicSessionProtocolOutput &output) {
  BEACON_TEST_REQUIRE(output.session_replies.size() == 1);
  stream_v1::SessionStreamEnvelope reply;
  BEACON_TEST_REQUIRE(reply.ParseFromArray(
      output.session_replies[0].data() + 4,
      static_cast<int>(output.session_replies[0].size() - 4)));
  return reply;
}

void stream_ids_have_one_unambiguous_role() {
  BEACON_TEST_REQUIRE(beacon::stream::classify_peer_stream(0) ==
                      QuicPeerStreamRole::session);
  BEACON_TEST_REQUIRE(beacon::stream::classify_peer_stream(2) ==
                      QuicPeerStreamRole::input);
  BEACON_TEST_REQUIRE(beacon::stream::classify_peer_stream(6) ==
                      QuicPeerStreamRole::feedback);
  BEACON_TEST_REQUIRE(beacon::stream::classify_peer_stream(4) ==
                      QuicPeerStreamRole::invalid);
  BEACON_TEST_REQUIRE(beacon::stream::classify_peer_stream(10) ==
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

  BEACON_TEST_REQUIRE(kept_open(first));
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
  BEACON_TEST_REQUIRE(second.accepted_authentication->maximum_datagram_bytes ==
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
  BEACON_TEST_REQUIRE(protocol_failed(replayed));
  BEACON_TEST_REQUIRE(
      reply_from(replayed).session_authenticated().error_code() ==
      stream_v1::SESSION_ERROR_CODE_TICKET_REPLAYED);

  auto wrong_version = authenticate("irrelevant");
  wrong_version.set_protocol_version(2);
  QuicSessionProtocol version(store);
  const auto rejected =
      version.receive(QuicPeerStreamRole::session, frame(wrong_version), 1'000);
  BEACON_TEST_REQUIRE(protocol_failed(rejected));
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
      kept_open(protocol.receive(QuicPeerStreamRole::session, auth, 1'000)));
  const auto started = protocol.receive(QuicPeerStreamRole::session,
                                        frame(start_session(2)), 1'000);
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStartSession>(started) !=
                      nullptr);

  stream_v1::SessionStreamEnvelope session;
  session.set_protocol_version(1);
  session.set_session_id("session-a");
  session.set_sequence(3);
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
  const auto *accepted_idr =
      accepted_action<AcceptedIdrRequest>(session_output);
  BEACON_TEST_REQUIRE(accepted_idr != nullptr);
  BEACON_TEST_REQUIRE(accepted_idr->session_generation == 1);
  BEACON_TEST_REQUIRE(accepted_idr->request.reason() ==
                      stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);
  BEACON_TEST_REQUIRE(input_output.packets.size() == 1);
  BEACON_TEST_REQUIRE(input_output.packets[0].channel ==
                      beacon::stream::StreamChannel::input);
  BEACON_TEST_REQUIRE(feedback_output.packets.size() == 1);
  BEACON_TEST_REQUIRE(feedback_output.packets[0].channel ==
                      beacon::stream::StreamChannel::feedback);
  BEACON_TEST_REQUIRE(input_output.inputs.size() == 1);
  BEACON_TEST_REQUIRE(input_output.inputs[0].session_generation == 1);
  BEACON_TEST_REQUIRE(input_output.inputs[0].input.SerializeAsString() ==
                      input.SerializeAsString());
  BEACON_TEST_REQUIRE(feedback_output.feedback.size() == 1);
  BEACON_TEST_REQUIRE(feedback_output.feedback[0].session_generation == 1);
  BEACON_TEST_REQUIRE(
      feedback_output.feedback[0].feedback.SerializeAsString() ==
      feedback.SerializeAsString());
}

void idr_requests_require_an_active_media_session_and_typed_reason() {
  AuthorizedQuicTicketStore before_start_store;
  BEACON_TEST_REQUIRE(
      before_start_store.authorize(grant("raw-ticket-idr-before-start")));
  QuicSessionProtocol before_start(before_start_store);
  BEACON_TEST_REQUIRE(kept_open(before_start.receive(
      QuicPeerStreamRole::session,
      frame(authenticate("raw-ticket-idr-before-start")), 1'000)));
  stream_v1::SessionStreamEnvelope premature;
  premature.set_protocol_version(1);
  premature.set_session_id("session-a");
  premature.set_sequence(2);
  premature.mutable_request_idr()->set_reason(
      stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);
  BEACON_TEST_REQUIRE(protocol_failed(before_start.receive(
      QuicPeerStreamRole::session, frame(premature), 1'000)));

  AuthorizedQuicTicketStore unspecified_store;
  BEACON_TEST_REQUIRE(
      unspecified_store.authorize(grant("raw-ticket-idr-unspecified")));
  QuicSessionProtocol unspecified(unspecified_store);
  BEACON_TEST_REQUIRE(kept_open(unspecified.receive(
      QuicPeerStreamRole::session,
      frame(authenticate("raw-ticket-idr-unspecified")), 1'000)));
  const auto started = unspecified.receive(QuicPeerStreamRole::session,
                                           frame(start_session(2)), 1'000);
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStartSession>(started) !=
                      nullptr);
  stream_v1::SessionStreamEnvelope invalid;
  invalid.set_protocol_version(1);
  invalid.set_session_id("session-a");
  invalid.set_sequence(3);
  invalid.mutable_request_idr();
  BEACON_TEST_REQUIRE(protocol_failed(unspecified.receive(
      QuicPeerStreamRole::session, frame(invalid), 1'000)));
}

void start_session_is_typed_once_per_authenticated_generation() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-start")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);

  const auto auth =
      protocol.receive(QuicPeerStreamRole::session,
                       frame(authenticate("raw-ticket-start")), 1'000);
  const auto first = protocol.receive(QuicPeerStreamRole::session,
                                      frame(start_session(2)), 1'000);
  const auto duplicate = protocol.receive(QuicPeerStreamRole::session,
                                          frame(start_session(3)), 1'000);

  BEACON_TEST_REQUIRE(auth.accepted_authentication.has_value());
  const auto *accepted_start = accepted_action<AcceptedStartSession>(first);
  BEACON_TEST_REQUIRE(accepted_start != nullptr);
  BEACON_TEST_REQUIRE(accepted_start->session_generation == 1);
  BEACON_TEST_REQUIRE(accepted_start->maximum_datagram_bytes == 1232);
  BEACON_TEST_REQUIRE(accepted_start->start_session.SerializeAsString() ==
                      start_session(2).start_session().SerializeAsString());
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStartSession>(duplicate) ==
                      nullptr);
}

void start_session_must_match_every_authorized_video_field() {
  using Mutation = std::function<void(stream_v1::SelectedVideoMode&)>;
  const std::vector<std::pair<std::string, Mutation>> mismatches{
      {"codec", [](auto& video) { video.set_codec(stream_v1::VIDEO_CODEC_HEVC); }},
      {"width", [](auto& video) { video.set_width(2561); }},
      {"height", [](auto& video) { video.set_height(1601); }},
      {"fps-numerator",
       [](auto& video) { video.set_frames_per_second_numerator(60); }},
      {"fps-denominator",
       [](auto& video) { video.set_frames_per_second_denominator(2); }},
      {"dynamic-range",
       [](auto& video) { video.set_dynamic_range(stream_v1::DYNAMIC_RANGE_HDR10); }},
  };

  for (const auto& [name, mutate] : mismatches) {
    AuthorizedQuicTicketStore store;
    const auto raw_ticket = "mismatched-video-" + name;
    BEACON_TEST_REQUIRE(store.authorize(grant(raw_ticket)));
    QuicSessionProtocol protocol(store);
    protocol.set_maximum_datagram_bytes(1232);
    BEACON_TEST_REQUIRE(
        protocol
            .receive(QuicPeerStreamRole::session,
                     frame(authenticate(raw_ticket)), 1'000)
            .accepted_authentication.has_value());
    auto start = start_session(2);
    mutate(*start.mutable_start_session()->mutable_selected_video());

    const auto rejected = protocol.receive(
        QuicPeerStreamRole::session, frame(start), 1'001);

    BEACON_TEST_REQUIRE(protocol_failed(rejected));
    BEACON_TEST_REQUIRE(
        accepted_action<AcceptedStartSession>(rejected) == nullptr);
  }
}

void stop_session_clears_active_state_before_another_idr_request() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-stop")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);
  BEACON_TEST_REQUIRE(protocol
                          .receive(QuicPeerStreamRole::session,
                                   frame(authenticate("raw-ticket-stop")),
                                   1'000)
                          .accepted_authentication.has_value());
  const auto started = protocol.receive(QuicPeerStreamRole::session,
                                        frame(start_session(2)), 1'001);
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStartSession>(started) !=
                      nullptr);

  const auto stopped = protocol.receive(QuicPeerStreamRole::session,
                                        frame(stop_session(3)), 1'002);
  const auto *accepted_stop = accepted_action<AcceptedStopSession>(stopped);
  BEACON_TEST_REQUIRE(accepted_stop != nullptr);
  BEACON_TEST_REQUIRE(accepted_stop->session_generation == 1);
  BEACON_TEST_REQUIRE(accepted_stop->stop_session.reason() ==
                      stream_v1::SESSION_STOP_REASON_CLIENT_REQUEST);
  BEACON_TEST_REQUIRE(
      stopped.connection_disposition ==
      ServerConnectionDisposition::session_complete);

  stream_v1::SessionStreamEnvelope idr;
  idr.set_protocol_version(1);
  idr.set_session_id("session-a");
  idr.set_sequence(4);
  idr.mutable_request_idr()->set_reason(
      stream_v1::IDR_REQUEST_REASON_DATAGRAM_LOSS);
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::session, frame(idr), 1'003)));
}

void coalesced_session_actions_preserve_protocol_order() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-coalesced")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);
  BEACON_TEST_REQUIRE(protocol
                          .receive(QuicPeerStreamRole::session,
                                   frame(authenticate("raw-ticket-coalesced")),
                                   1'000)
                          .accepted_authentication.has_value());

  auto bytes = frame(start_session(2));
  const auto idr = frame(request_idr(3));
  const auto stop = frame(stop_session(4));
  bytes.insert(bytes.end(), idr.begin(), idr.end());
  bytes.insert(bytes.end(), stop.begin(), stop.end());

  const auto output =
      protocol.receive(QuicPeerStreamRole::session, bytes, 1'001);

  BEACON_TEST_REQUIRE(
      output.connection_disposition ==
      ServerConnectionDisposition::session_complete);
  BEACON_TEST_REQUIRE(output.accepted_session_actions.size() == 3);
  BEACON_TEST_REQUIRE(
      std::holds_alternative<
          QuicSessionProtocolOutput::AcceptedStartSession>(
          output.accepted_session_actions[0]));
  BEACON_TEST_REQUIRE(
      std::holds_alternative<
          QuicSessionProtocolOutput::AcceptedIdrRequest>(
          output.accepted_session_actions[1]));
  BEACON_TEST_REQUIRE(
      std::holds_alternative<
          QuicSessionProtocolOutput::AcceptedStopSession>(
          output.accepted_session_actions[2]));
}

void benchmark_start_and_cancel_are_typed_for_the_authenticated_generation() {
  AuthorizedQuicTicketStore store;
  auto authorization = grant("benchmark-ticket");
  authorization.selected_video.reset();
  authorization.benchmark_plan = start_benchmark(2).start_benchmark();
  BEACON_TEST_REQUIRE(store.authorize(std::move(authorization)));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1200);

  auto authenticated =
      protocol.receive(QuicPeerStreamRole::session,
                       frame(authenticate("benchmark-ticket")), 1'000);
  BEACON_TEST_REQUIRE(authenticated.accepted_authentication.has_value());

  auto started = protocol.receive(QuicPeerStreamRole::session,
                                  frame(start_benchmark(2)), 1'001);
  BEACON_TEST_REQUIRE(kept_open(started));
  const auto *accepted_start = accepted_action<AcceptedStartBenchmark>(started);
  BEACON_TEST_REQUIRE(accepted_start != nullptr);
  BEACON_TEST_REQUIRE(
      accepted_start->session_generation ==
      authenticated.accepted_authentication->session_generation);
  BEACON_TEST_REQUIRE(accepted_start->start_benchmark.run_id() ==
                      "11111111-1111-1111-1111-111111111111");
  BEACON_TEST_REQUIRE(
      accepted_start->start_benchmark.datagram_round().packet_count() == 8);
  BEACON_TEST_REQUIRE(accepted_start->start_benchmark.run_token().size() == 16);

  auto canceled = protocol.receive(QuicPeerStreamRole::session,
                                   frame(cancel_benchmark(3)), 1'002);
  BEACON_TEST_REQUIRE(kept_open(canceled));
  const auto *accepted_cancel =
      accepted_action<AcceptedCancelBenchmark>(canceled);
  BEACON_TEST_REQUIRE(accepted_cancel != nullptr);
  BEACON_TEST_REQUIRE(
      accepted_cancel->session_generation ==
      authenticated.accepted_authentication->session_generation);
  BEACON_TEST_REQUIRE(accepted_cancel->cancel_benchmark.run_id() ==
                      "11111111-1111-1111-1111-111111111111");
}

void benchmark_stop_is_a_terminal_session_action() {
  AuthorizedQuicTicketStore store;
  auto authorization = grant("benchmark-stop-ticket");
  authorization.selected_video.reset();
  authorization.benchmark_plan = start_benchmark(2).start_benchmark();
  BEACON_TEST_REQUIRE(store.authorize(std::move(authorization)));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1200);
  BEACON_TEST_REQUIRE(protocol
                          .receive(QuicPeerStreamRole::session,
                                   frame(authenticate("benchmark-stop-ticket")),
                                   1'000)
                          .accepted_authentication.has_value());
  BEACON_TEST_REQUIRE(
      accepted_action<AcceptedStartBenchmark>(protocol.receive(
          QuicPeerStreamRole::session, frame(start_benchmark(2)), 1'001)) !=
      nullptr);

  const auto stopped = protocol.receive(
      QuicPeerStreamRole::session, frame(stop_session(3)), 1'002);

  BEACON_TEST_REQUIRE(
      stopped.connection_disposition ==
      ServerConnectionDisposition::session_complete);
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStopSession>(stopped) != nullptr);
}

void benchmark_start_must_match_the_worker_authorized_plan() {
  AuthorizedQuicTicketStore store;
  auto authorization = grant("modified-benchmark-ticket");
  authorization.selected_video.reset();
  authorization.benchmark_plan = start_benchmark(2).start_benchmark();
  BEACON_TEST_REQUIRE(store.authorize(std::move(authorization)));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1200);
  BEACON_TEST_REQUIRE(
      protocol
          .receive(QuicPeerStreamRole::session,
                   frame(authenticate("modified-benchmark-ticket")), 1'000)
          .accepted_authentication.has_value());

  auto modified = start_benchmark(2);
  modified.mutable_start_benchmark()
      ->mutable_datagram_round()
      ->set_packet_count(9);
  auto rejected =
      protocol.receive(QuicPeerStreamRole::session, frame(modified), 1'001);
  BEACON_TEST_REQUIRE(protocol_failed(rejected));
  BEACON_TEST_REQUIRE(accepted_action<AcceptedStartBenchmark>(rejected) ==
                      nullptr);
}

void reset_and_fresh_authentication_allocate_a_new_generation() {
  AuthorizedQuicTicketStore store;
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-generation-a")));
  BEACON_TEST_REQUIRE(store.authorize(grant("raw-ticket-generation-b")));
  QuicSessionProtocol protocol(store);
  protocol.set_maximum_datagram_bytes(1232);

  const auto first =
      protocol.receive(QuicPeerStreamRole::session,
                       frame(authenticate("raw-ticket-generation-a")), 1'000);
  protocol.reset();
  protocol.set_maximum_datagram_bytes(1232);
  const auto second =
      protocol.receive(QuicPeerStreamRole::session,
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
  stale_input.mutable_input_batch()
      ->add_events()
      ->mutable_keyboard()
      ->set_scan_code(31);
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
    was_observed =
        !bytes.empty() && std::ranges::all_of(bytes, [](std::byte value) {
          return value == std::byte{};
        });
  };

  beacon::stream::secure_clear_bytes(pending, observer, &observed);

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
      observation.all_zero && std::ranges::all_of(bytes, [](std::byte value) {
        return value == std::byte{};
      });
}

void oversized_partial_authentication_is_wiped_before_buffer_reuse() {
  AuthorizedQuicTicketStore store;
  ProtocolWipeObservation observation;
  QuicSessionProtocol protocol(store, observe_protocol_wipe, &observation);
  const std::array partial_auth{std::byte{0x01}, std::byte{0x7f},
                                std::byte{0x55}};
  BEACON_TEST_REQUIRE(kept_open(protocol.receive(
      QuicPeerStreamRole::session, partial_auth, 1'000)));

  const std::vector oversized(
      static_cast<std::size_t>(beacon::stream::maximum_stream_message_bytes) +
          5U,
      std::byte{0x33});
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::session, oversized, 1'000)));
  BEACON_TEST_REQUIRE(observation.nonempty_wipes == 1);
  BEACON_TEST_REQUIRE(observation.all_zero);
}

void malformed_authentication_is_wiped_before_logical_clear() {
  AuthorizedQuicTicketStore store;
  ProtocolWipeObservation observation;
  QuicSessionProtocol protocol(store, observe_protocol_wipe, &observation);
  const std::array malformed_length{std::byte{0x00}, std::byte{0x00},
                                    std::byte{0x00}, std::byte{0x00}};
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::session, malformed_length, 1'000)));

  const std::array malformed_frame{std::byte{0x00}, std::byte{0x00},
                                   std::byte{0x00}, std::byte{0x01},
                                   std::byte{0xff}};
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::session, malformed_frame, 1'000)));
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
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::input, frame(input), 1'000)));

  protocol.reset();
  const std::array<std::byte, 4> oversized{std::byte{0}, std::byte{0x10},
                                           std::byte{0}, std::byte{1}};
  BEACON_TEST_REQUIRE(protocol_failed(protocol.receive(
      QuicPeerStreamRole::session, oversized, 1'000)));
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    stream_ids_have_one_unambiguous_role();
    fragmented_authentication_consumes_ticket_and_returns_negotiated_limit();
    replay_and_version_mismatch_return_typed_rejections();
    authenticated_streams_are_routed_independently();
    idr_requests_require_an_active_media_session_and_typed_reason();
    start_session_is_typed_once_per_authenticated_generation();
    start_session_must_match_every_authorized_video_field();
    stop_session_clears_active_state_before_another_idr_request();
    coalesced_session_actions_preserve_protocol_order();
    benchmark_start_and_cancel_are_typed_for_the_authenticated_generation();
    benchmark_stop_is_a_terminal_session_action();
    benchmark_start_must_match_the_worker_authorized_plan();
    reset_and_fresh_authentication_allocate_a_new_generation();
    stale_old_connection_receive_does_not_touch_current_protocol_state();
    secure_clear_observes_zeroes_before_pending_bytes_are_released();
    oversized_partial_authentication_is_wiped_before_buffer_reuse();
    malformed_authentication_is_wiped_before_logical_clear();
    unauthenticated_data_and_oversized_frames_fail_closed();
  });
}
