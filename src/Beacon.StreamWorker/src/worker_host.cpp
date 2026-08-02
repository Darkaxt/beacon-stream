#include "beacon/worker/worker_host.h"

#include <array>
#include <limits>
#include <optional>
#include <string>
#include <utility>

namespace beacon::worker {
namespace {

std::string bytes_to_string(const std::vector<std::byte>& bytes) {
  std::string result;
  result.reserve(bytes.size());
  for (const auto value : bytes) {
    result.push_back(static_cast<char>(value));
  }
  return result;
}

std::optional<std::uint32_t> bitrate_bps(std::uint32_t bitrate_kbps) {
  constexpr std::uint32_t bits_per_kilobit{1'000};
  if (bitrate_kbps == 0 ||
      bitrate_kbps >
          std::numeric_limits<std::uint32_t>::max() / bits_per_kilobit) {
    return std::nullopt;
  }
  return bitrate_kbps * bits_per_kilobit;
}

std::optional<video::WorkerVideoPlan>
worker_video_plan_from(const v1::WorkerIpcEnvelope& request) {
  const auto& source = request.prepare_session();
  const auto minimum = bitrate_bps(source.minimum_bitrate_kbps());
  const auto initial = bitrate_bps(source.initial_bitrate_kbps());
  const auto maximum = bitrate_bps(source.maximum_bitrate_kbps());
  if (!minimum || !initial || !maximum || *minimum > *initial ||
      *initial > *maximum) {
    return std::nullopt;
  }
  video::WorkerVideoPlan plan{
      .session_id = request.session_id(),
      .display_device_name = std::wstring(source.display_device_name().begin(),
                                          source.display_device_name().end()),
      .width = source.width(),
      .height = source.height(),
      .frame_rate_numerator = source.frames_per_second_numerator(),
      .frame_rate_denominator = source.frames_per_second_denominator(),
      .minimum_bitrate_bps = *minimum,
      .initial_bitrate_bps = *initial,
      .maximum_bitrate_bps = *maximum,
  };
  return video::valid_worker_video_plan(plan) ? std::optional{std::move(plan)}
                                              : std::nullopt;
}

std::optional<stream::v1::SelectedAudioMode>
worker_audio_plan_from(const v1::WorkerIpcEnvelope& request) {
  const auto& source = request.prepare_session();
  if (source.audio_codec() != v1::WORKER_AUDIO_CODEC_OPUS ||
      source.audio_sample_rate_hz() != 48'000 ||
      source.audio_channel_count() != 2 ||
      source.audio_frame_duration_us() != 20'000 ||
      source.audio_bitrate_bps() != 96'000) {
    return std::nullopt;
  }
  stream::v1::SelectedAudioMode plan;
  plan.set_codec(stream::v1::AUDIO_CODEC_OPUS);
  plan.set_sample_rate_hz(source.audio_sample_rate_hz());
  plan.set_channel_count(source.audio_channel_count());
  plan.set_frame_duration_us(source.audio_frame_duration_us());
  plan.set_bitrate_bps(source.audio_bitrate_bps());
  return plan;
}

}  // namespace

WorkerHost::WorkerHost(std::vector<std::byte> worker_instance_id,
                       std::uint32_t process_id,
                       IWorkerMediaTransport& transport,
                       AuthorizedQuicTicketStore& authorized_tickets,
                       video::IWorkerVideoPipeline& video_pipeline,
                       video::ProductionVideoCapabilities video_capabilities)
    : worker_instance_id_(std::move(worker_instance_id)),
      process_id_(process_id),
      transport_(transport),
      authorized_tickets_(authorized_tickets),
      video_pipeline_(video_pipeline),
      video_capabilities_(video_capabilities) {}

v1::WorkerIpcEnvelope WorkerHost::hello() const {
  v1::WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(worker_protocol_version);
  auto* message = envelope.mutable_worker_hello();
  message->set_worker_instance_id(bytes_to_string(worker_instance_id_));
  message->set_process_id(process_id_);
  return envelope;
}

v1::WorkerIpcEnvelope WorkerHost::capabilities() const {
  v1::WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(worker_protocol_version);
  auto* message = envelope.mutable_worker_capabilities();
  message->set_worker_instance_id(bytes_to_string(worker_instance_id_));
  message->add_video_codecs(v1::WORKER_VIDEO_CODEC_H264);
  message->add_video_encoders(v1::WORKER_VIDEO_ENCODER_NVENC);
  message->add_capture_methods(
      v1::WORKER_CAPTURE_METHOD_WINDOWS_GRAPHICS_CAPTURE);
  message->set_quic_datagrams(true);
  message->set_maximum_sessions(1);
  message->set_maximum_frames_per_second(120);
  message->set_hdr10(false);
  message->set_video_available(video_capabilities_.available);
  message->add_audio_codecs(v1::WORKER_AUDIO_CODEC_OPUS);
  message->add_audio_capture_methods(
      v1::WORKER_AUDIO_CAPTURE_METHOD_WASAPI_LOOPBACK);
  message->set_audio_available(false);
  message->set_audio_unavailable_boundary(
      v1::DIAGNOSTIC_BOUNDARY_AUDIO_CAPTURE);
  if (!video_capabilities_.available) {
    const auto boundary = video_capabilities_.unavailable_boundary ==
                                  video::ProductionVideoCapabilityBoundary::capture
                              ? v1::DIAGNOSTIC_BOUNDARY_CAPTURE
                              : v1::DIAGNOSTIC_BOUNDARY_ENCODER;
    message->set_video_unavailable_boundary(boundary);
    message->set_video_unavailable_code(video_capabilities_.unavailable_code);
  }
  return envelope;
}

v1::WorkerIpcEnvelope WorkerHost::ready() const {
  v1::WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(worker_protocol_version);
  envelope.mutable_worker_ready()->set_worker_instance_id(
      bytes_to_string(worker_instance_id_));
  return envelope;
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::dispatch(
    const v1::WorkerIpcEnvelope& request) {
  if (request.protocol_version() != worker_protocol_version) {
    return reject(request, v1::WORKER_ERROR_CODE_UNSUPPORTED_VERSION);
  }
  if (shutdown_requested_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  if (request.request_id() == 0) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }

  switch (request.body_case()) {
    case v1::WorkerIpcEnvelope::kPrepareSession:
      return prepare(request);
    case v1::WorkerIpcEnvelope::kPrepareBenchmark:
      return prepare_benchmark(request);
    case v1::WorkerIpcEnvelope::kAuthorizeTicket:
      return authorize_ticket(request);
    case v1::WorkerIpcEnvelope::kRevokeTicket:
      return revoke_ticket(request);
    case v1::WorkerIpcEnvelope::kStartMedia:
      return start_media(request);
    case v1::WorkerIpcEnvelope::kStopMedia:
      return stop_media(request);
    case v1::WorkerIpcEnvelope::kRequestIdr:
      return request_idr(request);
    case v1::WorkerIpcEnvelope::kShutdownWorker:
      return shutdown(request);
    default:
      return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }
}

bool WorkerHost::shutdown_requested() const noexcept { return shutdown_requested_; }

std::size_t WorkerHost::authorized_ticket_count() const {
  return authorized_tickets_.size();
}

v1::WorkerIpcEnvelope WorkerHost::response_envelope(
    const v1::WorkerIpcEnvelope& request) const {
  v1::WorkerIpcEnvelope response;
  response.set_protocol_version(worker_protocol_version);
  response.set_request_id(request.request_id());
  response.set_session_id(request.session_id());
  return response;
}

v1::WorkerIpcEnvelope WorkerHost::completion(const v1::WorkerIpcEnvelope& request,
                                             bool succeeded,
                                             v1::WorkerErrorCode error_code) const {
  auto response = response_envelope(request);
  auto* result = response.mutable_worker_completion();
  result->set_succeeded(succeeded);
  result->set_error_code(error_code);
  return response;
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::reject(
    const v1::WorkerIpcEnvelope& request,
    v1::WorkerErrorCode error_code) const {
  return {completion(request, false, error_code)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::prepare(
    const v1::WorkerIpcEnvelope& request) {
  const auto& plan = request.prepare_session();
  if (request.session_id().empty() || plan.display_target().empty() ||
      plan.display_device_name().empty() || plan.width() == 0 ||
      plan.height() == 0 || plan.frames_per_second_numerator() == 0 ||
      plan.frames_per_second_denominator() == 0 ||
      plan.video_codec() != v1::WORKER_VIDEO_CODEC_H264 ||
      plan.dynamic_range() != v1::WORKER_DYNAMIC_RANGE_SDR) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }
  if (streaming_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  auto video_plan = worker_video_plan_from(request);
  auto audio_plan = worker_audio_plan_from(request);
  if (!video_plan || !audio_plan) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }
  try {
    if (!video_pipeline_.prepare(*video_plan)) {
      return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
    }
  } catch (...) {
    return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  }

  prepared_ = true;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  session_id_ = request.session_id();
  prepared_video_plan_ = std::move(video_plan);
  prepared_audio_plan_ = std::move(audio_plan);
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_PREPARED);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  return {std::move(state), completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::prepare_benchmark(
    const v1::WorkerIpcEnvelope& request) {
  const auto& plan = request.prepare_benchmark().plan();
  const auto valid_round = [](const stream::v1::BenchmarkRoundPlan& round) {
    return round.packet_count() != 0 && round.payload_bytes() != 0 &&
           round.measurement_interval_us() != 0;
  };
  if (request.session_id().empty() || plan.run_id().empty() ||
      plan.schema_version() == 0 || plan.run_token().size() != 16 ||
      !valid_round(plan.reliable_round()) ||
      !valid_round(plan.datagram_round()) || streaming_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }

  video_pipeline_.reset();
  prepared_video_plan_.reset();
  prepared_audio_plan_.reset();
  prepared_ = true;
  benchmark_prepared_ = true;
  benchmark_plan_ = plan;
  session_id_ = request.session_id();
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(
      v1::WORKER_SESSION_STATE_PREPARED);
  state.mutable_session_state_changed()->set_error_code(
      v1::WORKER_ERROR_CODE_NONE);
  return {std::move(state),
          completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::authorize_ticket(
    const v1::WorkerIpcEnvelope& request) {
  const auto& ticket = request.authorize_ticket();
  if (!prepared_ || request.session_id() != session_id_ ||
      request.session_id().empty() || ticket.ticket_hash().size() != 32 ||
      ticket.client_id().empty() || ticket.plan_revision() == 0 ||
      ticket.expires_at_unix_ms() == 0 ||
      ticket.worker_instance_id() != bytes_to_string(worker_instance_id_)) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }

  TicketHash hash{};
  for (std::size_t index = 0; index < hash.size(); ++index) {
    hash[index] = static_cast<std::byte>(ticket.ticket_hash()[index]);
  }
  AuthorizedQuicTicket authorization{
          .hash = hash,
          .client_id = ticket.client_id(),
          .session_id = request.session_id(),
          .plan_revision = ticket.plan_revision(),
          .expires_at_unix_ms = ticket.expires_at_unix_ms(),
          .selected_video = std::nullopt,
          .selected_audio = std::nullopt,
          .benchmark_plan = std::nullopt,
      };
  if (benchmark_prepared_) {
    authorization.benchmark_plan = benchmark_plan_;
  } else if (prepared_video_plan_ && prepared_audio_plan_) {
    authorization.selected_video =
        video::selected_video_from_plan(*prepared_video_plan_);
    authorization.selected_audio = *prepared_audio_plan_;
  }
  if (!authorized_tickets_.authorize(std::move(authorization))) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::revoke_ticket(
    const v1::WorkerIpcEnvelope& request) {
  const auto& hash = request.revoke_ticket().ticket_hash();
  if (hash.size() != 32) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }

  authorized_tickets_.revoke({reinterpret_cast<const std::byte*>(hash.data()), hash.size()});
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::start_media(
    const v1::WorkerIpcEnvelope& request) {
  if (!prepared_ || streaming_ || request.session_id() != session_id_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  const auto& media = request.start_media();
  if (media.listen_port() > 65'535 ||
      !transport_.configure_listener(
          media.listen_address(), static_cast<std::uint16_t>(media.listen_port())) ||
      !transport_.open_connection()) {
    return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  }
  const auto listener_port = transport_.local_port();
  if (listener_port == 0) {
    transport_.close_connection();
    return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  }

  streaming_ = true;
  auto transport_ready = response_envelope(request);
  transport_ready.mutable_worker_transport_ready()->set_listener_port(listener_port);
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_STREAMING);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  auto metrics = response_envelope(request);
  metrics.mutable_media_metrics()->set_encoded_frames(0);
  metrics.mutable_media_metrics()->set_sent_datagrams(0);
  metrics.mutable_media_metrics()->set_bytes_sent(0);
  return {std::move(transport_ready), std::move(state), std::move(metrics),
          completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::stop_media(
    const v1::WorkerIpcEnvelope& request) {
  if (!streaming_ || request.session_id() != session_id_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  video_pipeline_.reset();
  transport_.close_connection();
  streaming_ = false;
  prepared_ = false;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  prepared_video_plan_.reset();
  prepared_audio_plan_.reset();
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_STOPPED);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  return {std::move(state), completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::request_idr(
    const v1::WorkerIpcEnvelope& request) {
  if (!streaming_ || benchmark_prepared_ || !prepared_video_plan_ ||
      request.session_id() != session_id_ ||
      request.request_idr().reason() == v1::IDR_REASON_UNSPECIFIED) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  try {
    if (!video_pipeline_.request_idr()) {
      return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
    }
  } catch (...) {
    return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  }
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::shutdown(
    const v1::WorkerIpcEnvelope& request) {
  video_pipeline_.reset();
  if (streaming_) {
    transport_.close_connection();
  }
  transport_.shutdown();
  streaming_ = false;
  prepared_ = false;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  prepared_video_plan_.reset();
  prepared_audio_plan_.reset();
  shutdown_requested_ = true;
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

}  // namespace beacon::worker
