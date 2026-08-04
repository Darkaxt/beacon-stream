#include "beacon/worker/video/worker_video_pipeline.h"
#include "beacon/worker/worker_events.h"
#include "beacon/worker/worker_host.h"

#include "../Beacon.StreamProtocol.Tests/test_failure.h"
#include "worker_ipc.pb.h"

#include <algorithm>
#include <cstddef>
#include <cstdint>
#include <type_traits>
#include <utility>
#include <vector>

namespace {

using beacon::stream::TransportPacket;
using beacon::stream::TransportSendResult;
using beacon::worker::AuthorizedQuicTicketStore;
using beacon::worker::IWorkerMediaTransport;
using beacon::worker::WorkerHost;
using beacon::worker::v1::WorkerIpcEnvelope;
namespace audio = beacon::worker::audio;
namespace video = beacon::worker::video;

static_assert(!std::is_base_of_v<beacon::stream::IStreamTransport,
                                 IWorkerMediaTransport>);

class RecordingTransport final : public IWorkerMediaTransport {
public:
  bool configure_listener(std::string_view address,
                          std::uint16_t port) override {
    listen_address = address;
    listen_port = port;
    return true;
  }

  std::uint16_t local_port() const noexcept override {
    return selected_port == 0 ? listen_port : selected_port;
  }

  bool open_connection() override {
    ++open_count;
    if (!open_result) {
      return false;
    }
    open = true;
    return true;
  }

  void close_connection() noexcept override {
    if (open) {
      open = false;
      ++close_count;
      if (lifecycle) {
        lifecycle->emplace_back("transport-close");
      }
    }
  }

  void request_active_disconnect() noexcept override { ++disconnect_count; }

  TransportSendResult
  send_for_generation(TransportPacket packet,
                      std::uint64_t session_generation) override {
    generations.push_back(session_generation);
    packets.push_back(std::move(packet));
    return TransportSendResult::accepted;
  }

  void shutdown() noexcept override {
    close_connection();
    ++shutdown_count;
    if (lifecycle) {
      lifecycle->emplace_back("transport-shutdown");
    }
  }

  bool open{};
  bool open_result{true};
  std::string listen_address;
  std::uint16_t listen_port{};
  std::uint16_t selected_port{};
  std::size_t open_count{};
  std::size_t close_count{};
  std::size_t disconnect_count{};
  std::size_t shutdown_count{};
  std::vector<TransportPacket> packets;
  std::vector<std::uint64_t> generations;
  std::vector<std::string> *lifecycle{};
};

class RecordingAudioPipeline final : public audio::IWorkerAudioPipeline {
public:
  bool prepare(const audio::WorkerAudioPlan &plan) override {
    plans.push_back(plan);
    return prepare_result;
  }

  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {
    ++media_event_count;
  }

  void reset() noexcept override {
    ++reset_count;
    if (lifecycle) {
      lifecycle->emplace_back("audio-pipeline-reset");
    }
  }

  bool prepare_result{true};
  std::size_t media_event_count{};
  std::size_t reset_count{};
  std::vector<audio::WorkerAudioPlan> plans;
  std::vector<std::string> *lifecycle{};
};

class RecordingPipeline final : public video::IWorkerVideoPipeline {
public:
  bool prepare(const video::WorkerVideoPlan &plan) override {
    plans.push_back(plan);
    return prepare_result;
  }

  void handle_media_event(const beacon::worker::QuicMediaEvent &) override {
    ++media_event_count;
  }

  bool request_idr() override {
    ++idr_count;
    return idr_result;
  }

  void reset() noexcept override {
    ++reset_count;
    if (lifecycle) {
      lifecycle->emplace_back("pipeline-reset");
    }
  }

  bool prepare_result{true};
  bool idr_result{true};
  std::size_t media_event_count{};
  std::size_t idr_count{};
  std::size_t reset_count{};
  std::vector<video::WorkerVideoPlan> plans;
  std::vector<std::string> *lifecycle{};
  RecordingAudioPipeline audio;
};

const WorkerIpcEnvelope &
completion(const std::vector<WorkerIpcEnvelope> &responses) {
  const auto response =
      std::ranges::find_if(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kWorkerCompletion;
      });
  BEACON_TEST_REQUIRE(response != responses.end());
  return *response;
}

WorkerIpcEnvelope command(std::uint64_t request_id, const char *session_id) {
  WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(1);
  envelope.set_request_id(request_id);
  envelope.set_session_id(session_id);
  return envelope;
}

WorkerIpcEnvelope prepare_video(std::uint64_t request_id = 20) {
  auto prepare = command(request_id, "session-a");
  auto *plan = prepare.mutable_prepare_session();
  plan->set_display_target("display-a");
  plan->set_display_device_name("\\\\.\\DISPLAY7");
  plan->set_video_codec(beacon::worker::v1::WORKER_VIDEO_CODEC_H264);
  plan->set_width(2560);
  plan->set_height(1600);
  plan->set_frames_per_second_numerator(120);
  plan->set_frames_per_second_denominator(1);
  plan->set_dynamic_range(beacon::worker::v1::WORKER_DYNAMIC_RANGE_SDR);
  plan->set_video_profile(beacon::stream::v1::VIDEO_PROFILE_H264_HIGH);
  plan->set_video_bit_depth(8);
  plan->set_color_primaries(beacon::stream::v1::COLOR_PRIMARIES_BT709);
  plan->set_transfer_function(beacon::stream::v1::TRANSFER_FUNCTION_BT709);
  plan->set_matrix_coefficients(beacon::stream::v1::MATRIX_COEFFICIENTS_BT709);
  plan->set_color_range(beacon::stream::v1::COLOR_RANGE_LIMITED);
  plan->set_audio_codec(beacon::worker::v1::WORKER_AUDIO_CODEC_OPUS);
  plan->set_audio_sample_rate_hz(48'000);
  plan->set_audio_channel_count(2);
  plan->set_audio_frame_duration_us(20'000);
  plan->set_audio_bitrate_bps(96'000);
  plan->set_minimum_bitrate_kbps(8'000);
  plan->set_initial_bitrate_kbps(24'000);
  plan->set_maximum_bitrate_kbps(40'000);
  return prepare;
}

video::ProductionVideoCapabilities available_video() {
  return {.available = true};
}

audio::ProductionAudioCapabilities available_audio();

WorkerIpcEnvelope prepare_hdr10(std::uint64_t request_id = 25) {
  auto prepare = prepare_video(request_id);
  auto* plan = prepare.mutable_prepare_session();
  plan->set_video_codec(beacon::worker::v1::WORKER_VIDEO_CODEC_HEVC);
  plan->set_dynamic_range(beacon::worker::v1::WORKER_DYNAMIC_RANGE_HDR10);
  plan->set_video_profile(beacon::stream::v1::VIDEO_PROFILE_HEVC_MAIN10);
  plan->set_video_bit_depth(10);
  plan->set_color_primaries(beacon::stream::v1::COLOR_PRIMARIES_BT2020);
  plan->set_transfer_function(beacon::stream::v1::TRANSFER_FUNCTION_PQ);
  plan->set_matrix_coefficients(
      beacon::stream::v1::MATRIX_COEFFICIENTS_BT2020_NON_CONSTANT_LUMINANCE);
  std::string metadata(25, '\0');
  metadata[17] = static_cast<char>(0xe8);
  metadata[18] = static_cast<char>(0x03);
  metadata[21] = static_cast<char>(0xe8);
  metadata[22] = static_cast<char>(0x03);
  metadata[23] = static_cast<char>(0x90);
  metadata[24] = static_cast<char>(0x01);
  plan->set_hdr_static_info(metadata);
  plan->set_hdr_static_info_in_bitstream(true);
  return prepare;
}

void exact_hdr10_capability_and_prepare_are_truthful() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio,
                  {.available = true, .hevc_main10_hdr10_available = true},
                  available_audio());

  const auto capabilities = host.capabilities().worker_capabilities();
  BEACON_TEST_REQUIRE(capabilities.hdr10());
  BEACON_TEST_REQUIRE(capabilities.video_codecs_size() == 2);
  BEACON_TEST_REQUIRE(capabilities.video_codecs(1) ==
                      beacon::worker::v1::WORKER_VIDEO_CODEC_HEVC);
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(prepare_hdr10())).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(pipeline.plans.size() == 1);
  BEACON_TEST_REQUIRE(pipeline.plans[0].codec ==
                      beacon::stream::v1::VIDEO_CODEC_HEVC);
  BEACON_TEST_REQUIRE(pipeline.plans[0].dynamic_range ==
                      beacon::stream::v1::DYNAMIC_RANGE_HDR10);
  BEACON_TEST_REQUIRE(pipeline.plans[0].hdr_static_info.size() == 25);
}

audio::ProductionAudioCapabilities available_audio() {
  return {.available = true};
}

void hello_capabilities_and_ready_are_typed_and_instance_bound() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}, std::byte{2}, std::byte{3}}, 42, transport,
                  tickets, pipeline, pipeline.audio, available_video(),
                  available_audio());

  const auto hello = host.hello();
  const auto capabilities = host.capabilities();
  const auto ready = host.ready();

  BEACON_TEST_REQUIRE(hello.protocol_version() == 1);
  BEACON_TEST_REQUIRE(hello.worker_hello().process_id() == 42);
  BEACON_TEST_REQUIRE(hello.worker_hello().worker_instance_id() ==
                      "\x01\x02\x03");
  BEACON_TEST_REQUIRE(capabilities.protocol_version() == 1);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().quic_datagrams());
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().maximum_sessions() ==
                      1);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().worker_instance_id() ==
                      "\x01\x02\x03");
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().video_codecs_size() ==
                      1);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().video_codecs(0) ==
                      beacon::worker::v1::WORKER_VIDEO_CODEC_H264);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().video_encoders_size() == 1);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().video_encoders(0) ==
                      beacon::worker::v1::WORKER_VIDEO_ENCODER_NVENC);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().capture_methods_size() == 1);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().capture_methods(0) ==
      beacon::worker::v1::WORKER_CAPTURE_METHOD_WINDOWS_GRAPHICS_CAPTURE);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().maximum_frames_per_second() == 120);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().video_available());
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().audio_codecs_size() ==
                      1);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().audio_codecs(0) ==
                      beacon::worker::v1::WORKER_AUDIO_CODEC_OPUS);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().audio_capture_methods_size() == 1);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().audio_capture_methods(0) ==
      beacon::worker::v1::WORKER_AUDIO_CAPTURE_METHOD_WASAPI_LOOPBACK);
  BEACON_TEST_REQUIRE(capabilities.worker_capabilities().audio_available());
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().video_unavailable_boundary() ==
      beacon::worker::v1::DIAGNOSTIC_BOUNDARY_UNSPECIFIED);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().video_unavailable_code() == 0);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().audio_unavailable_boundary() ==
      beacon::worker::v1::DIAGNOSTIC_BOUNDARY_UNSPECIFIED);
  BEACON_TEST_REQUIRE(
      capabilities.worker_capabilities().audio_unavailable_code() == 0);
  BEACON_TEST_REQUIRE(ready.protocol_version() == 1);
  BEACON_TEST_REQUIRE(ready.worker_ready().worker_instance_id() ==
                      "\x01\x02\x03");
}

void unavailable_video_is_reported_without_poisoning_worker() {
  constexpr std::uint32_t encoder_unavailable_code{7};
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio,
                  {.available = false,
                   .unavailable_boundary =
                       video::ProductionVideoCapabilityBoundary::encoder,
                   .unavailable_code = encoder_unavailable_code},
                  available_audio());

  const auto capabilities = host.capabilities().worker_capabilities();

  BEACON_TEST_REQUIRE(!capabilities.video_available());
  BEACON_TEST_REQUIRE(capabilities.video_unavailable_boundary() ==
                      beacon::worker::v1::DIAGNOSTIC_BOUNDARY_ENCODER);
  BEACON_TEST_REQUIRE(capabilities.video_unavailable_code() ==
                      encoder_unavailable_code);
  const auto rejected = completion(host.dispatch(prepare_video()));
  BEACON_TEST_REQUIRE(!rejected.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(rejected.worker_completion().error_code() ==
                      beacon::worker::v1::WORKER_ERROR_CODE_CAPABILITY_UNAVAILABLE);
  BEACON_TEST_REQUIRE(pipeline.plans.empty());
  BEACON_TEST_REQUIRE(!host.shutdown_requested());
}

void unavailable_audio_is_reported_without_preparing_a_session() {
  constexpr std::uint32_t capture_unavailable_code{0x88890004U};
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(),
                  {.available = false,
                   .unavailable_boundary =
                       audio::ProductionAudioCapabilityBoundary::capture,
                   .unavailable_code = capture_unavailable_code});

  const auto capabilities = host.capabilities().worker_capabilities();
  BEACON_TEST_REQUIRE(!capabilities.audio_available());
  BEACON_TEST_REQUIRE(capabilities.audio_unavailable_boundary() ==
                      beacon::worker::v1::DIAGNOSTIC_BOUNDARY_AUDIO_CAPTURE);
  BEACON_TEST_REQUIRE(capabilities.audio_unavailable_code() ==
                      capture_unavailable_code);
  const auto rejected = completion(host.dispatch(prepare_video()));
  BEACON_TEST_REQUIRE(!rejected.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(rejected.worker_completion().error_code() ==
                      beacon::worker::v1::WORKER_ERROR_CODE_CAPABILITY_UNAVAILABLE);
  BEACON_TEST_REQUIRE(pipeline.plans.empty());
  BEACON_TEST_REQUIRE(pipeline.audio.plans.empty());
}

void unsupported_versions_receive_one_correlated_failure() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());
  auto request = command(17, "session-a");
  request.set_protocol_version(2);
  request.mutable_prepare_session();

  const auto responses = host.dispatch(request);
  const auto &result = completion(responses);

  BEACON_TEST_REQUIRE(result.request_id() == 17);
  BEACON_TEST_REQUIRE(result.session_id() == "session-a");
  BEACON_TEST_REQUIRE(!result.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(
      result.worker_completion().error_code() ==
      beacon::worker::v1::WORKER_ERROR_CODE_UNSUPPORTED_VERSION);
  BEACON_TEST_REQUIRE(
      std::ranges::count_if(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kWorkerCompletion;
      }) == 1);
}

void marker_ready_state_does_not_claim_encoded_or_sent_media() {
  RecordingTransport transport;
  transport.selected_port = 45999;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());

  auto prepare = prepare_video();
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(prepare)).worker_completion().succeeded());
  const std::vector expected_plans{video::WorkerVideoPlan{
      .session_id = "session-a",
      .display_device_name = L"\\\\.\\DISPLAY7",
      .width = 2560,
      .height = 1600,
      .frame_rate_numerator = 120,
      .frame_rate_denominator = 1,
      .minimum_bitrate_bps = 8'000'000,
      .initial_bitrate_bps = 24'000'000,
      .maximum_bitrate_bps = 40'000'000,
  }};
  BEACON_TEST_REQUIRE(pipeline.plans == expected_plans);
  const std::vector expected_audio_plans{audio::WorkerAudioPlan{
      .session_id = "session-a",
      .sample_rate_hz = 48'000,
      .channel_count = 2,
      .frame_duration_us = 20'000,
      .bitrate_bps = 96'000,
  }};
  BEACON_TEST_REQUIRE(pipeline.audio.plans == expected_audio_plans);

  auto start = command(21, "session-a");
  start.mutable_start_media()->set_listen_port(0);
  const auto responses = host.dispatch(start);

  BEACON_TEST_REQUIRE(completion(responses).request_id() == 21);
  BEACON_TEST_REQUIRE(completion(responses).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(transport.open_count == 1);
  BEACON_TEST_REQUIRE(transport.listen_address.empty());
  BEACON_TEST_REQUIRE(transport.listen_port == 0);
  BEACON_TEST_REQUIRE(transport.packets.empty());
  BEACON_TEST_REQUIRE(
      std::ranges::count_if(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kWorkerTransportReady;
      }) == 1);
  BEACON_TEST_REQUIRE(
      std::ranges::any_of(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kWorkerTransportReady &&
               value.worker_transport_ready().listener_port() == 45999;
      }));
  BEACON_TEST_REQUIRE(
      std::ranges::any_of(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kMediaMetrics &&
               value.media_metrics().encoded_frames() == 0 &&
               value.media_metrics().sent_datagrams() == 0 &&
               value.media_metrics().dropped_frames() == 0 &&
               value.media_metrics().bytes_sent() == 0;
      }));
}

void failed_listener_open_emits_no_transport_ready_event() {
  RecordingTransport transport;
  transport.open_result = false;
  transport.selected_port = 45999;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());

  auto prepare = prepare_video(22);
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(prepare)).worker_completion().succeeded());

  auto start = command(23, "session-a");
  start.mutable_start_media()->set_listen_port(0);
  const auto responses = host.dispatch(start);

  BEACON_TEST_REQUIRE(!completion(responses).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(
      std::ranges::none_of(responses, [](const WorkerIpcEnvelope &value) {
        return value.body_case() == WorkerIpcEnvelope::kWorkerTransportReady;
      }));
}

void prepare_rejects_missing_windows_display_device_name() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());
  auto prepare = command(24, "session-a");
  auto *plan = prepare.mutable_prepare_session();
  plan->set_display_target("display-a");
  plan->set_video_codec(beacon::worker::v1::WORKER_VIDEO_CODEC_H264);
  plan->set_width(2560);
  plan->set_height(1600);
  plan->set_frames_per_second_numerator(120);
  plan->set_frames_per_second_denominator(1);
  plan->set_dynamic_range(beacon::worker::v1::WORKER_DYNAMIC_RANGE_SDR);

  const auto responses = host.dispatch(prepare);
  const auto &result = completion(responses);

  BEACON_TEST_REQUIRE(!result.worker_completion().succeeded());
  BEACON_TEST_REQUIRE(result.worker_completion().error_code() ==
                      beacon::worker::v1::WORKER_ERROR_CODE_INVALID_REQUEST);
}

void benchmark_plan_is_prepared_without_a_display_or_video_mode() {
  RecordingTransport transport;
  transport.selected_port = 46001;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());

  auto prepare = command(24, "benchmark-session");
  auto *plan = prepare.mutable_prepare_benchmark()->mutable_plan();
  plan->set_run_id("11111111-1111-1111-1111-111111111111");
  plan->set_schema_version(3);
  plan->set_run_token(std::string(16, '\x2a'));
  plan->mutable_reliable_round()->set_packet_count(4);
  plan->mutable_reliable_round()->set_payload_bytes(1024);
  plan->mutable_reliable_round()->set_measurement_interval_us(500'000);
  plan->mutable_datagram_round()->set_packet_count(8);
  plan->mutable_datagram_round()->set_payload_bytes(1000);
  plan->mutable_datagram_round()->set_measurement_interval_us(500'000);

  const auto prepared = host.dispatch(prepare);
  BEACON_TEST_REQUIRE(completion(prepared).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(pipeline.reset_count == 1);
  BEACON_TEST_REQUIRE(pipeline.plans.empty());

  auto start = command(25, "benchmark-session");
  start.mutable_start_media()->set_listen_port(0);
  const auto started = host.dispatch(start);
  BEACON_TEST_REQUIRE(completion(started).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(transport.open_count == 1);
}

void ticket_authorization_is_hash_only_and_worker_bound() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}, std::byte{2}}, 42, transport, tickets,
                  pipeline, pipeline.audio, available_video(),
                  available_audio());
  BEACON_TEST_REQUIRE(completion(host.dispatch(prepare_video()))
                          .worker_completion()
                          .succeeded());
  auto authorize = command(25, "session-a");
  auto *ticket = authorize.mutable_authorize_ticket();
  const std::string raw_ticket = "worker-bound-ticket";
  const auto ticket_hash = beacon::worker::hash_stream_ticket(
      {reinterpret_cast<const std::byte *>(raw_ticket.data()),
       raw_ticket.size()});
  ticket->set_ticket_hash(ticket_hash.data(), ticket_hash.size());
  ticket->set_client_id("z-fold-7");
  ticket->set_plan_revision(8);
  ticket->set_expires_at_unix_ms(1'800'000'000'000ULL);
  ticket->set_worker_instance_id("\x01\x02");

  const auto accepted = host.dispatch(authorize);
  const auto consumed =
      tickets.authorize({reinterpret_cast<const std::byte *>(raw_ticket.data()),
                         raw_ticket.size()},
                        "z-fold-7", "session-a", 8, 1'000);
  auto revoke = command(26, "session-a");
  revoke.mutable_revoke_ticket()->set_ticket_hash(ticket_hash.data(),
                                                  ticket_hash.size());
  const auto revoked = host.dispatch(revoke);
  ticket->set_worker_instance_id("\x09");
  const auto rejected = host.dispatch(authorize);

  BEACON_TEST_REQUIRE(completion(accepted).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(
      consumed.result ==
      beacon::stream::StreamTicketAuthorizationResult::accepted);
  BEACON_TEST_REQUIRE(consumed.selected_video.has_value());
  BEACON_TEST_REQUIRE(consumed.selected_video->width() == 2560);
  BEACON_TEST_REQUIRE(consumed.selected_video->height() == 1600);
  BEACON_TEST_REQUIRE(consumed.selected_audio.has_value());
  BEACON_TEST_REQUIRE(consumed.selected_audio->codec() ==
                      beacon::stream::v1::AUDIO_CODEC_OPUS);
  BEACON_TEST_REQUIRE(consumed.selected_audio->sample_rate_hz() == 48'000);
  BEACON_TEST_REQUIRE(completion(revoked).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.authorized_ticket_count() == 0);
  BEACON_TEST_REQUIRE(!completion(rejected).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.authorized_ticket_count() == 0);
}

void bitrate_order_and_pipeline_prepare_failures_are_rejected() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());

  auto unordered = prepare_video();
  unordered.mutable_prepare_session()->set_minimum_bitrate_kbps(30'000);
  BEACON_TEST_REQUIRE(
      !completion(host.dispatch(unordered)).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(pipeline.plans.empty());

  pipeline.prepare_result = false;
  BEACON_TEST_REQUIRE(!completion(host.dispatch(prepare_video(21)))
                           .worker_completion()
                           .succeeded());
  BEACON_TEST_REQUIRE(pipeline.plans.size() == 1);

  pipeline.prepare_result = true;
  pipeline.audio.prepare_result = false;
  BEACON_TEST_REQUIRE(!completion(host.dispatch(prepare_video(22)))
                           .worker_completion()
                           .succeeded());
  BEACON_TEST_REQUIRE(pipeline.plans.size() == 2);
  BEACON_TEST_REQUIRE(pipeline.audio.plans.size() == 1);
  BEACON_TEST_REQUIRE(pipeline.reset_count == 1);
}

void idr_stop_and_shutdown_follow_pipeline_lifecycle_order() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  std::vector<std::string> lifecycle;
  transport.lifecycle = &lifecycle;
  pipeline.lifecycle = &lifecycle;
  pipeline.audio.lifecycle = &lifecycle;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());
  BEACON_TEST_REQUIRE(completion(host.dispatch(prepare_video()))
                          .worker_completion()
                          .succeeded());
  auto start = command(21, "session-a");
  start.mutable_start_media()->set_listen_port(50000);
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(start)).worker_completion().succeeded());

  auto idr = command(22, "session-a");
  idr.mutable_request_idr()->set_reason(
      beacon::worker::v1::IDR_REASON_CLIENT_RECOVERY);
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(idr)).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(pipeline.idr_count == 1);

  auto stop = command(23, "session-a");
  stop.mutable_stop_media()->set_reason(
      beacon::worker::v1::STOP_MEDIA_REASON_EXPLICIT);
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(stop)).worker_completion().succeeded());
  const std::vector expected_stop{std::string{"pipeline-reset"},
                                  std::string{"audio-pipeline-reset"},
                                  std::string{"transport-close"}};
  BEACON_TEST_REQUIRE(lifecycle == expected_stop);

  lifecycle.clear();
  auto shutdown = command(24, "");
  shutdown.mutable_shutdown_worker();
  BEACON_TEST_REQUIRE(
      completion(host.dispatch(shutdown)).worker_completion().succeeded());
  const std::vector expected_shutdown{std::string{"pipeline-reset"},
                                      std::string{"audio-pipeline-reset"},
                                      std::string{"transport-shutdown"}};
  BEACON_TEST_REQUIRE(lifecycle == expected_shutdown);
}

void explicit_shutdown_is_acknowledged_and_releases_once() {
  RecordingTransport transport;
  RecordingPipeline pipeline;
  AuthorizedQuicTicketStore tickets;
  WorkerHost host({std::byte{1}}, 42, transport, tickets, pipeline,
                  pipeline.audio, available_video(), available_audio());
  auto shutdown = command(30, "");
  shutdown.mutable_shutdown_worker();

  const auto first = host.dispatch(shutdown);
  const auto second = host.dispatch(shutdown);

  BEACON_TEST_REQUIRE(completion(first).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(!completion(second).worker_completion().succeeded());
  BEACON_TEST_REQUIRE(host.shutdown_requested());
  BEACON_TEST_REQUIRE(transport.shutdown_count == 1);
  BEACON_TEST_REQUIRE(pipeline.reset_count == 1);
  BEACON_TEST_REQUIRE(pipeline.audio.reset_count == 1);
}

void video_failure_events_preserve_stage_and_platform_status() {
  const auto events = beacon::worker::make_video_pipeline_failure_events({
      .session_id = "session-a",
      .session_generation = 31,
      .boundary = video::VideoPipelineFailureBoundary::capture,
      .failure_stage = "capture-session-create",
      .native_code = 0x80070005U,
  });

  BEACON_TEST_REQUIRE(events.size() == 2);
  const auto &diagnostic = events[1].worker_diagnostic();
  BEACON_TEST_REQUIRE(diagnostic.failure_stage() == "capture-session-create");
  BEACON_TEST_REQUIRE(diagnostic.platform_error_code() == 0x80070005U);
}

void audio_failure_events_preserve_boundary_stage_and_platform_status() {
  const auto events = beacon::worker::make_audio_pipeline_failure_events({
      .session_id = "session-a",
      .session_generation = 32,
      .boundary = audio::AudioPipelineFailureBoundary::capture,
      .failure_stage = "packet-acquire",
      .native_code = 0x88890004U,
  });

  BEACON_TEST_REQUIRE(events.size() == 2);
  const auto &diagnostic = events[1].worker_diagnostic();
  BEACON_TEST_REQUIRE(diagnostic.boundary() ==
                      beacon::worker::v1::DIAGNOSTIC_BOUNDARY_AUDIO_CAPTURE);
  BEACON_TEST_REQUIRE(diagnostic.failure_stage() == "packet-acquire");
  BEACON_TEST_REQUIRE(diagnostic.platform_error_code() == 0x88890004U);
}

} // namespace

int main() {
  return beacon::stream::testing::run_tests([] {
    hello_capabilities_and_ready_are_typed_and_instance_bound();
    exact_hdr10_capability_and_prepare_are_truthful();
    unavailable_video_is_reported_without_poisoning_worker();
    unavailable_audio_is_reported_without_preparing_a_session();
    unsupported_versions_receive_one_correlated_failure();
    marker_ready_state_does_not_claim_encoded_or_sent_media();
    failed_listener_open_emits_no_transport_ready_event();
    prepare_rejects_missing_windows_display_device_name();
    benchmark_plan_is_prepared_without_a_display_or_video_mode();
    ticket_authorization_is_hash_only_and_worker_bound();
    bitrate_order_and_pipeline_prepare_failures_are_rejected();
    idr_stop_and_shutdown_follow_pipeline_lifecycle_order();
    explicit_shutdown_is_acknowledged_and_releases_once();
    video_failure_events_preserve_stage_and_platform_status();
    audio_failure_events_preserve_boundary_stage_and_platform_status();
  });
}
