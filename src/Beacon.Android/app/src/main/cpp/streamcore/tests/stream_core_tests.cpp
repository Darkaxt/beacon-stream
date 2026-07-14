#include "stream_core.h"

#include "stream_control.pb.h"
#include "beacon/stream/media_datagram.h"
#include "test_failure.h"

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <new>
#include <optional>
#include <span>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>

namespace {

namespace android_stream = beacon::android::streamcore;
namespace stream_v1 = beacon::stream::v1;

std::vector<std::byte> payload(std::span<const std::byte> framed) {
  BEACON_TEST_REQUIRE(framed.size() >= 4);
  const auto size = (std::to_integer<std::uint32_t>(framed[0]) << 24U) |
                    (std::to_integer<std::uint32_t>(framed[1]) << 16U) |
                    (std::to_integer<std::uint32_t>(framed[2]) << 8U) |
                    std::to_integer<std::uint32_t>(framed[3]);
  BEACON_TEST_REQUIRE(framed.size() == size + 4U);
  return {framed.begin() + 4, framed.end()};
}

struct FakeTransport final : android_stream::Transport {
  struct Send {
    android_stream::StreamRole role;
    std::vector<std::byte> bytes;
  };

  bool connect(const android_stream::Endpoint &value) override {
    ++connect_count;
    endpoint = value;
    return true;
  }
  bool open_stream(android_stream::StreamRole role) override {
    opened.push_back(role);
    return true;
  }
  bool send(android_stream::StreamRole role,
            std::vector<std::byte> bytes) override {
    if (throw_on_send) throw std::runtime_error("injected send failure");
    sends.push_back({role, std::move(bytes)});
    return true;
  }
  void shutdown() override {
    ++shutdown_count;
    if (throw_on_shutdown) throw std::runtime_error("injected shutdown failure");
  }
  void release() override { ++release_count; }

  android_stream::Endpoint endpoint;
  std::vector<android_stream::StreamRole> opened;
  std::vector<Send> sends;
  int shutdown_count{};
  int release_count{};
  int connect_count{};
  bool throw_on_send{};
  bool throw_on_shutdown{};
};

struct FakeSink final : android_stream::FrameSink {
  void frame(android_stream::EncodedFrame value) override {
    frames.push_back(std::move(value));
  }
  void state_changed(android_stream::State value) override {
    if (throw_on_state) throw std::runtime_error("injected sink failure");
    states.push_back(value);
  }

  std::vector<android_stream::EncodedFrame> frames;
  std::vector<android_stream::State> states;
  bool throw_on_state{};
};

#ifndef NDEBUG
bool fail_assembler_allocation;
std::optional<android_stream::CloseFaultPoint> close_fault;

void inject_start_fault(android_stream::StartFaultPoint point) {
  if (fail_assembler_allocation &&
      point == android_stream::StartFaultPoint::assembler_allocation) {
    throw std::bad_alloc();
  }
}

void inject_close_fault(android_stream::CloseFaultPoint point) {
  if (close_fault != point) return;
  if (point == android_stream::CloseFaultPoint::stop_envelope_allocation) {
    throw std::bad_alloc();
  }
  throw std::runtime_error("injected serialization failure");
}
#endif

android_stream::ConnectionGrant grant() {
  android_stream::ConnectionGrant result;
  result.endpoint.host = "beacon.example";
  result.endpoint.port = 47990;
  result.endpoint.spki_pin.fill(std::byte{0xA5});
  result.client_id = "z-fold-7";
  result.session_id = "session-1";
  result.plan_revision = 12;
  result.ticket = android_stream::TicketSecret({std::byte{1}, std::byte{2}, std::byte{3}});
  result.video = {.codec = stream_v1::VIDEO_CODEC_H264,
                  .width = 1920,
                  .height = 1080,
                  .fps_numerator = 60,
                  .fps_denominator = 1,
                  .dynamic_range = stream_v1::DYNAMIC_RANGE_SDR};
  return result;
}

android_stream::ConnectionGrant benchmark_grant() {
  auto result = grant();
  result.benchmark = android_stream::BenchmarkGrant{
      .run_id = "11111111-1111-1111-1111-111111111111",
      .run_token = {std::byte{0x00}, std::byte{0x01}, std::byte{0x02},
                    std::byte{0x03}, std::byte{0x04}, std::byte{0x05},
                    std::byte{0x06}, std::byte{0x07}, std::byte{0x08},
                    std::byte{0x09}, std::byte{0x0a}, std::byte{0x0b},
                    std::byte{0x0c}, std::byte{0x0d}, std::byte{0x0e},
                    std::byte{0x0f}},
      .schema_version = 3,
      .reliable_packet_count = 4,
      .reliable_payload_bytes = 1024,
      .reliable_measurement_interval_us = 500'000,
      .datagram_packet_count = 8,
      .datagram_payload_bytes = 1000,
      .datagram_measurement_interval_us = 500'000};
  return result;
}

stream_v1::SessionStreamEnvelope parse_session(std::span<const std::byte> bytes) {
  const auto message_bytes = payload(bytes);
  stream_v1::SessionStreamEnvelope message;
  BEACON_TEST_REQUIRE(message.ParseFromArray(message_bytes.data(),
                                 static_cast<int>(message_bytes.size())));
  return message;
}

stream_v1::FeedbackStreamEnvelope parse_feedback(
    std::span<const std::byte> bytes) {
  const auto message_bytes = payload(bytes);
  stream_v1::FeedbackStreamEnvelope message;
  BEACON_TEST_REQUIRE(message.ParseFromArray(
      message_bytes.data(), static_cast<int>(message_bytes.size())));
  return message;
}

std::vector<std::byte> accepted_reply(std::uint64_t sequence = 1) {
  stream_v1::SessionStreamEnvelope reply;
  reply.set_protocol_version(1);
  reply.set_session_id("session-1");
  reply.set_sequence(sequence);
  reply.mutable_session_authenticated()->set_accepted(true);
  reply.mutable_session_authenticated()->set_error_code(
      stream_v1::SESSION_ERROR_CODE_NONE);
  reply.mutable_session_authenticated()->set_maximum_datagram_bytes(1200);
  std::vector<std::byte> bytes(4U + reply.ByteSizeLong());
  const auto size = static_cast<std::uint32_t>(reply.ByteSizeLong());
  bytes[0] = static_cast<std::byte>(size >> 24U);
  bytes[1] = static_cast<std::byte>(size >> 16U);
  bytes[2] = static_cast<std::byte>(size >> 8U);
  bytes[3] = static_cast<std::byte>(size);
  BEACON_TEST_REQUIRE(reply.SerializeToArray(bytes.data() + 4, static_cast<int>(size)));
  return bytes;
}

std::vector<std::byte> framed_session(
    const stream_v1::SessionStreamEnvelope &message) {
  std::vector<std::byte> bytes(4U + message.ByteSizeLong());
  const auto size = static_cast<std::uint32_t>(message.ByteSizeLong());
  bytes[0] = static_cast<std::byte>(size >> 24U);
  bytes[1] = static_cast<std::byte>(size >> 16U);
  bytes[2] = static_cast<std::byte>(size >> 8U);
  bytes[3] = static_cast<std::byte>(size);
  BEACON_TEST_REQUIRE(
      message.SerializeToArray(bytes.data() + 4, static_cast<int>(size)));
  return bytes;
}

std::vector<std::byte> reliable_benchmark_chunk(std::uint64_t envelope_sequence,
                                                 std::uint64_t packet_sequence) {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-1");
  message.set_sequence(envelope_sequence);
  auto *chunk = message.mutable_benchmark_reliable_chunk();
  chunk->set_run_id("11111111-1111-1111-1111-111111111111");
  chunk->set_round_id(1);
  chunk->set_sequence(packet_sequence);
  chunk->set_sent_at_us(100 + packet_sequence);
  chunk->set_payload(std::string(1024, 'b'));
  return framed_session(message);
}

std::vector<std::byte> benchmark_completed() {
  stream_v1::SessionStreamEnvelope message;
  message.set_protocol_version(1);
  message.set_session_id("session-1");
  message.set_sequence(6);
  auto *completed = message.mutable_benchmark_round_completed();
  completed->set_run_id("11111111-1111-1111-1111-111111111111");
  completed->set_round_id(2);
  completed->set_expected_packet_count(8);
  completed->set_payload_bytes(1000);
  for (std::uint64_t sequence = 0; sequence < 7; ++sequence) {
    auto *rtt = completed->add_rtt_observations();
    rtt->set_sequence(sequence);
    rtt->set_rtt_us(2'000 + sequence);
  }
  return framed_session(message);
}

std::vector<std::byte> media_datagram(
    std::uint64_t sequence, std::uint32_t frame_bytes,
    std::uint16_t chunk_index, std::uint16_t chunk_count,
    std::uint32_t payload_offset, std::span<const std::byte> bytes,
    beacon::stream::MediaDatagramFlags flags =
        beacon::stream::MediaDatagramFlags::none) {
  beacon::stream::MediaDatagramHeader header{
      .version = beacon::stream::media_datagram_version,
      .media_kind = beacon::stream::MediaKind::video,
      .flags = flags,
      .sequence = sequence,
      .presentation_time_us = sequence * 1000,
      .frame_bytes = frame_bytes,
      .chunk_index = chunk_index,
      .chunk_count = chunk_count,
      .payload_offset = payload_offset,
      .payload_bytes = static_cast<std::uint16_t>(bytes.size()),
  };
  std::vector<std::byte> result(
      beacon::stream::media_datagram_header_bytes + bytes.size());
  BEACON_TEST_REQUIRE(beacon::stream::serialize_media_datagram_header(
      header,
      std::span<std::byte, beacon::stream::media_datagram_header_bytes>{
          result.data(), beacon::stream::media_datagram_header_bytes}));
  std::ranges::copy(bytes,
                    result.begin() + beacon::stream::media_datagram_header_bytes);
  return result;
}

std::vector<std::byte> benchmark_datagram(
    const std::array<std::byte, 16> &run_token, std::uint64_t sequence,
    std::uint64_t sent_at_us, std::uint32_t payload_bytes) {
  std::vector<std::byte> result(
      beacon::stream::benchmark_datagram_header_bytes + payload_bytes,
      std::byte{0x44});
  const beacon::stream::BenchmarkDatagramHeader header{
      .run_token = run_token,
      .round_id = 2,
      .sequence = sequence,
      .sent_at_us = sent_at_us,
      .payload_bytes = payload_bytes};
  BEACON_TEST_REQUIRE(beacon::stream::serialize_benchmark_datagram_header(
      header,
      std::span<std::byte, beacon::stream::benchmark_datagram_header_bytes>{
          result.data(), beacon::stream::benchmark_datagram_header_bytes}));
  return result;
}

void starts_one_route_and_consumes_ticket() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  auto connection = grant();
  BEACON_TEST_REQUIRE(core.start(std::move(connection)));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE((transport.opened == std::vector{
      android_stream::StreamRole::session,
      android_stream::StreamRole::input,
      android_stream::StreamRole::feedback}));
  BEACON_TEST_REQUIRE(transport.sends.size() == 1);
  const auto auth = parse_session(transport.sends[0].bytes);
  BEACON_TEST_REQUIRE(auth.sequence() == 1);
  BEACON_TEST_REQUIRE(auth.authenticate_session().stream_ticket() == "\x01\x02\x03");
  BEACON_TEST_REQUIRE(core.ticket_consumed());
}

void accepted_auth_starts_selected_video() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::streaming);
  BEACON_TEST_REQUIRE(transport.sends.size() == 2);
  const auto start = parse_session(transport.sends[1].bytes);
  BEACON_TEST_REQUIRE(start.sequence() == 2);
  BEACON_TEST_REQUIRE(start.start_session().selected_video().width() == 1920);
  BEACON_TEST_REQUIRE(start.start_session().selected_video().height() == 1080);
  BEACON_TEST_REQUIRE(start.start_session().selected_video().frames_per_second_numerator() == 60);
}

void accepted_auth_starts_benchmark_without_starting_video() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(benchmark_grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::benchmarking);
  BEACON_TEST_REQUIRE(transport.sends.size() == 2);
  const auto start = parse_session(transport.sends[1].bytes);
  BEACON_TEST_REQUIRE(
      start.body_case() == stream_v1::SessionStreamEnvelope::kStartBenchmark);
  BEACON_TEST_REQUIRE(start.start_benchmark().run_id() ==
                      "11111111-1111-1111-1111-111111111111");
  BEACON_TEST_REQUIRE(start.start_benchmark().schema_version() == 3);
  BEACON_TEST_REQUIRE(start.start_benchmark().run_token().size() == 16);
  BEACON_TEST_REQUIRE(
      start.start_benchmark().reliable_round().packet_count() == 4);
  BEACON_TEST_REQUIRE(
      start.start_benchmark().datagram_round().packet_count() == 8);
}

void benchmark_packets_produce_echoes_and_explicit_completion_evidence() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(benchmark_grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply(), 500));
  for (std::uint64_t sequence = 0; sequence < 4; ++sequence) {
    BEACON_TEST_REQUIRE(core.receive_session(
        reliable_benchmark_chunk(2 + sequence, sequence), 1'000 + sequence * 500));
  }

  const auto run_token = benchmark_grant().benchmark->run_token;
  for (std::uint64_t sequence = 0; sequence < 7; ++sequence) {
    BEACON_TEST_REQUIRE(core.receive_datagram(
        benchmark_datagram(run_token, sequence, 100 + sequence * 10, 1000),
        2'000 + sequence * 12));
  }
  BEACON_TEST_REQUIRE(transport.sends.size() == 9);
  for (std::size_t index = 2; index < transport.sends.size(); ++index) {
    BEACON_TEST_REQUIRE(transport.sends[index].role ==
                        android_stream::StreamRole::feedback);
    stream_v1::FeedbackStreamEnvelope echo;
    const auto message_bytes = payload(transport.sends[index].bytes);
    BEACON_TEST_REQUIRE(echo.ParseFromArray(
        message_bytes.data(), static_cast<int>(message_bytes.size())));
    BEACON_TEST_REQUIRE(echo.benchmark_datagram_echo().sequence() == index - 2);
  }

  BEACON_TEST_REQUIRE(core.receive_session(benchmark_completed(), 4'000));
  auto result = core.take_benchmark_result();
  BEACON_TEST_REQUIRE(result.has_value());
  BEACON_TEST_REQUIRE(result->samples.size() == 8);
  BEACON_TEST_REQUIRE(result->received_datagrams == 7);
  BEACON_TEST_REQUIRE(!result->samples[7].received);
  BEACON_TEST_REQUIRE(!core.take_benchmark_result().has_value());
}

void benchmark_stop_cancels_and_allows_a_fresh_run() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(benchmark_grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));

  core.stop();
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::stopped);
  BEACON_TEST_REQUIRE(transport.sends.size() == 3);
  const auto cancel = parse_session(transport.sends[2].bytes);
  BEACON_TEST_REQUIRE(cancel.body_case() ==
                      stream_v1::SessionStreamEnvelope::kCancelBenchmark);
  BEACON_TEST_REQUIRE(cancel.cancel_benchmark().run_id() ==
                      "11111111-1111-1111-1111-111111111111");
  BEACON_TEST_REQUIRE(core.start(benchmark_grant()));
}

void accepted_auth_forwards_every_selected_video_mode_exactly() {
  struct ExpectedMode {
    stream_v1::VideoCodec codec;
    stream_v1::DynamicRange dynamic_range;
  };
  for (const auto expected : std::array{
           ExpectedMode{stream_v1::VIDEO_CODEC_H264,
                        stream_v1::DYNAMIC_RANGE_SDR},
           ExpectedMode{stream_v1::VIDEO_CODEC_HEVC,
                        stream_v1::DYNAMIC_RANGE_HDR10},
           ExpectedMode{stream_v1::VIDEO_CODEC_AV1,
                        stream_v1::DYNAMIC_RANGE_HDR10}}) {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    auto connection = grant();
    connection.video = {.codec = expected.codec,
                        .width = 2560,
                        .height = 1600,
                        .fps_numerator = 120,
                        .fps_denominator = 1,
                        .dynamic_range = expected.dynamic_range};
    BEACON_TEST_REQUIRE(core.start(std::move(connection)));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
    const auto start = parse_session(transport.sends[1].bytes);
    const auto &actual = start.start_session().selected_video();
    BEACON_TEST_REQUIRE(actual.codec() == expected.codec);
    BEACON_TEST_REQUIRE(actual.width() == 2560);
    BEACON_TEST_REQUIRE(actual.height() == 1600);
    BEACON_TEST_REQUIRE(actual.frames_per_second_numerator() == 120);
    BEACON_TEST_REQUIRE(actual.frames_per_second_denominator() == 1);
    BEACON_TEST_REQUIRE(actual.dynamic_range() == expected.dynamic_range);
  }
}

void sequences_are_monotonic_per_typed_channel() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));

  stream_v1::InputBatch input;
  input.add_events()->mutable_keyboard()->set_scan_code(1);
  BEACON_TEST_REQUIRE(core.send_input(input));
  BEACON_TEST_REQUIRE(core.send_input(input));
  stream_v1::QueueDepthFeedback queue;
  queue.set_queued_access_units(1);
  BEACON_TEST_REQUIRE(core.send_feedback(queue));
  BEACON_TEST_REQUIRE(core.send_feedback(queue));

  const auto first_input_bytes = payload(transport.sends[2].bytes);
  const auto second_input_bytes = payload(transport.sends[3].bytes);
  stream_v1::InputStreamEnvelope first_input;
  stream_v1::InputStreamEnvelope second_input;
  BEACON_TEST_REQUIRE(first_input.ParseFromArray(first_input_bytes.data(), first_input_bytes.size()));
  BEACON_TEST_REQUIRE(second_input.ParseFromArray(second_input_bytes.data(), second_input_bytes.size()));
  BEACON_TEST_REQUIRE(first_input.sequence() == 1 && second_input.sequence() == 2);

  const auto first_feedback_bytes = payload(transport.sends[4].bytes);
  const auto second_feedback_bytes = payload(transport.sends[5].bytes);
  stream_v1::FeedbackStreamEnvelope first_feedback;
  stream_v1::FeedbackStreamEnvelope second_feedback;
  BEACON_TEST_REQUIRE(first_feedback.ParseFromArray(first_feedback_bytes.data(), first_feedback_bytes.size()));
  BEACON_TEST_REQUIRE(second_feedback.ParseFromArray(second_feedback_bytes.data(), second_feedback_bytes.size()));
  BEACON_TEST_REQUIRE(first_feedback.sequence() == 1 && second_feedback.sequence() == 2);
}

void decoder_feedback_and_reset_requests_preserve_typed_payloads() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));

  stream_v1::DecoderFeedback decoder;
  decoder.set_state(stream_v1::DECODER_STATE_FAILED);
  decoder.set_platform_error_code(321);
  BEACON_TEST_REQUIRE(core.send_feedback(decoder));

  stream_v1::RenderedFrameFeedback rendered;
  rendered.set_frame_sequence(44);
  rendered.set_presentation_time_us(55);
  rendered.set_rendered_at_us(66);
  BEACON_TEST_REQUIRE(core.send_feedback(rendered));
  BEACON_TEST_REQUIRE(core.request_idr(
      stream_v1::IDR_REQUEST_REASON_DECODER_RESET, 44));

  const auto decoder_envelope = parse_feedback(transport.sends[2].bytes);
  BEACON_TEST_REQUIRE(decoder_envelope.sequence() == 1);
  BEACON_TEST_REQUIRE(decoder_envelope.decoder().state() ==
                      stream_v1::DECODER_STATE_FAILED);
  BEACON_TEST_REQUIRE(decoder_envelope.decoder().platform_error_code() == 321);

  const auto rendered_envelope = parse_feedback(transport.sends[3].bytes);
  BEACON_TEST_REQUIRE(rendered_envelope.sequence() == 2);
  BEACON_TEST_REQUIRE(rendered_envelope.rendered_frame().frame_sequence() == 44);
  BEACON_TEST_REQUIRE(
      rendered_envelope.rendered_frame().presentation_time_us() == 55);
  BEACON_TEST_REQUIRE(rendered_envelope.rendered_frame().rendered_at_us() == 66);

  const auto request = parse_session(transport.sends[4].bytes);
  BEACON_TEST_REQUIRE(request.request_idr().reason() ==
                      stream_v1::IDR_REQUEST_REASON_DECODER_RESET);
  BEACON_TEST_REQUIRE(request.request_idr().last_complete_sequence() == 44);
}

void frame_limit_is_derived_from_selected_resolution() {
  BEACON_TEST_REQUIRE(android_stream::derive_maximum_frame_bytes(320, 180) == 1024U * 1024U);
  BEACON_TEST_REQUIRE(android_stream::derive_maximum_frame_bytes(1920, 1080) == 6220800U);
  BEACON_TEST_REQUIRE(android_stream::derive_maximum_frame_bytes(7680, 4320) ==
          android_stream::maximum_planned_frame_bytes);

  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  auto low_resolution = grant();
  low_resolution.video.width = 320;
  low_resolution.video.height = 180;
  BEACON_TEST_REQUIRE(core.start(std::move(low_resolution)));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
  constexpr std::array one{std::byte{1}};
  BEACON_TEST_REQUIRE(!core.receive_datagram(media_datagram(
      1, android_stream::minimum_planned_frame_bytes + 1U,
      0, 2, 0, one)));
  BEACON_TEST_REQUIRE(core.receive_datagram(media_datagram(
      2, android_stream::minimum_planned_frame_bytes,
      0, 2, 0, one)));
}

void assembler_loss_requests_one_idr_until_recovery() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
  constexpr std::array one{std::byte{1}};
  for (std::uint64_t sequence = 10; sequence <= 14; ++sequence) {
    core.receive_datagram(media_datagram(sequence, 2, 0, 2, 0, one));
  }
  BEACON_TEST_REQUIRE(transport.sends.size() == 3);
  const auto first_request = parse_session(transport.sends[2].bytes);
  BEACON_TEST_REQUIRE(first_request.sequence() == 3);
  BEACON_TEST_REQUIRE(first_request.body_case() ==
          stream_v1::SessionStreamEnvelope::kRequestIdr);
  BEACON_TEST_REQUIRE(first_request.request_idr().reason() ==
          stream_v1::IDR_REQUEST_REASON_FRAME_EVICTED);
  BEACON_TEST_REQUIRE(first_request.request_idr().last_complete_sequence() == 0);
  core.receive_datagram(media_datagram(15, 2, 0, 2, 0, one));
  BEACON_TEST_REQUIRE(transport.sends.size() == 3);
  BEACON_TEST_REQUIRE(core.receive_datagram(media_datagram(
      20, 1, 0, 1, 0, one,
      beacon::stream::MediaDatagramFlags::idr |
          beacon::stream::MediaDatagramFlags::end_of_access_unit)));
  for (std::uint64_t sequence = 21; sequence <= 25; ++sequence) {
    core.receive_datagram(media_datagram(sequence, 2, 0, 2, 0, one));
  }
  BEACON_TEST_REQUIRE(transport.sends.size() == 4);
  const auto second_request = parse_session(transport.sends[3].bytes);
  BEACON_TEST_REQUIRE(second_request.sequence() == 4);
  BEACON_TEST_REQUIRE(second_request.request_idr().last_complete_sequence() == 20);
}

void capacity_eviction_recovered_by_same_idr_sends_no_request() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
  constexpr std::array one{std::byte{1}};
  for (std::uint64_t sequence = 10; sequence <= 13; ++sequence) {
    BEACON_TEST_REQUIRE(core.receive_datagram(media_datagram(sequence, 2, 0, 2, 0, one)));
  }
  BEACON_TEST_REQUIRE(core.receive_datagram(media_datagram(
      14, 1, 0, 1, 0, one,
      beacon::stream::MediaDatagramFlags::idr |
          beacon::stream::MediaDatagramFlags::codec_configuration |
          beacon::stream::MediaDatagramFlags::end_of_access_unit)));
  BEACON_TEST_REQUIRE(transport.sends.size() == 2);
  BEACON_TEST_REQUIRE(sink.frames.size() == 1);
  BEACON_TEST_REQUIRE(sink.frames[0].sequence == 14);
  BEACON_TEST_REQUIRE(sink.frames[0].idr);
  BEACON_TEST_REQUIRE(sink.frames[0].codec_configuration);
}

void close_faults_never_skip_transport_release() {
#ifndef NDEBUG
  for (const auto fault : {
           android_stream::CloseFaultPoint::stop_envelope_allocation,
           android_stream::CloseFaultPoint::stop_serialization}) {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
    close_fault = fault;
    android_stream::set_close_fault_hook_for_test(inject_close_fault);
    core.release();
    android_stream::set_close_fault_hook_for_test(nullptr);
    close_fault.reset();
    BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
    BEACON_TEST_REQUIRE(transport.release_count == 1);
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::released);
  }
#endif

  {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
    transport.throw_on_send = true;
    core.release();
    BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
    BEACON_TEST_REQUIRE(transport.release_count == 1);
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::released);
  }
  {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
    sink.throw_on_state = true;
    transport.throw_on_shutdown = true;
    core.release();
    core.release();
    BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
    BEACON_TEST_REQUIRE(transport.release_count == 1);
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::released);
  }
}

void authentication_reply_requires_state_and_exact_sequence() {
  {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(!core.receive_session(accepted_reply()));
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::failed);
  }
  for (const std::uint64_t invalid_sequence : {0ULL, 2ULL}) {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(!core.receive_session(accepted_reply(invalid_sequence)));
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::failed);
    BEACON_TEST_REQUIRE(transport.sends.size() == 1);
  }
  {
    FakeTransport transport;
    FakeSink sink;
    android_stream::StreamCore core(transport, sink);
    BEACON_TEST_REQUIRE(core.start(grant()));
    BEACON_TEST_REQUIRE(core.on_connected());
    BEACON_TEST_REQUIRE(core.receive_session(accepted_reply()));
    BEACON_TEST_REQUIRE(!core.receive_session(accepted_reply()));
    BEACON_TEST_REQUIRE(core.state() == android_stream::State::failed);
  }
}

void connection_loss_and_release_are_idempotent() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  core.on_connection_lost();
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::failed);
  core.stop();
  core.stop();
  core.release();
  core.release();
  BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
  BEACON_TEST_REQUIRE(transport.release_count == 1);
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::released);
}

void reconnect_reuses_the_one_core_after_connection_loss() {
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  core.on_connection_lost();
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::connecting);
  BEACON_TEST_REQUIRE(transport.connect_count == 2);
}

void failed_assembler_allocation_preserves_existing_core_ownership() {
#ifndef NDEBUG
  FakeTransport transport;
  FakeSink sink;
  android_stream::StreamCore core(transport, sink);
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.on_connected());
  BEACON_TEST_REQUIRE(core.ticket_consumed());
  core.on_connection_lost();

  fail_assembler_allocation = true;
  android_stream::set_start_fault_hook_for_test(inject_start_fault);
  try {
    static_cast<void>(core.start(grant()));
    BEACON_TEST_REQUIRE(false);
  } catch (const std::bad_alloc &) {
  }
  android_stream::set_start_fault_hook_for_test(nullptr);
  fail_assembler_allocation = false;

  BEACON_TEST_REQUIRE(core.state() == android_stream::State::failed);
  if (!core.ticket_consumed()) {
    std::fputs("assembler allocation failure replaced the active grant\n", stderr);
    BEACON_TEST_REQUIRE(false);
  }
  BEACON_TEST_REQUIRE(core.start(grant()));
  BEACON_TEST_REQUIRE(core.state() == android_stream::State::connecting);
#endif
}

}  // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    starts_one_route_and_consumes_ticket();
    accepted_auth_starts_selected_video();
    accepted_auth_starts_benchmark_without_starting_video();
    benchmark_packets_produce_echoes_and_explicit_completion_evidence();
    benchmark_stop_cancels_and_allows_a_fresh_run();
    accepted_auth_forwards_every_selected_video_mode_exactly();
    sequences_are_monotonic_per_typed_channel();
    decoder_feedback_and_reset_requests_preserve_typed_payloads();
    frame_limit_is_derived_from_selected_resolution();
    assembler_loss_requests_one_idr_until_recovery();
    close_faults_never_skip_transport_release();
    capacity_eviction_recovered_by_same_idr_sends_no_request();
    authentication_reply_requires_state_and_exact_sequence();
    connection_loss_and_release_are_idempotent();
    reconnect_reuses_the_one_core_after_connection_loss();
    failed_assembler_allocation_preserves_existing_core_ownership();
  });
}
