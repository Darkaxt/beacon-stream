#include "beacon/worker/worker_host.h"

#include <array>
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

}  // namespace

WorkerHost::WorkerHost(std::vector<std::byte> worker_instance_id,
                       std::uint32_t process_id,
                       IWorkerMediaTransport& transport,
                       AuthorizedQuicTicketStore& authorized_tickets)
    : worker_instance_id_(std::move(worker_instance_id)),
      process_id_(process_id),
      transport_(transport),
      authorized_tickets_(authorized_tickets) {}

v1::WorkerIpcEnvelope WorkerHost::hello() const {
  v1::WorkerIpcEnvelope envelope;
  envelope.set_protocol_version(worker_protocol_version);
  auto* message = envelope.mutable_worker_hello();
  message->set_worker_instance_id(bytes_to_string(worker_instance_id_));
  message->set_process_id(process_id_);
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
      return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
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
  if (request.session_id().empty() || plan.display_target().empty() || plan.width() == 0 ||
      plan.height() == 0 || plan.frames_per_second_numerator() == 0 ||
      plan.frames_per_second_denominator() == 0 ||
      plan.video_codec() != v1::WORKER_VIDEO_CODEC_H264 ||
      plan.dynamic_range() != v1::WORKER_DYNAMIC_RANGE_SDR) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }
  if (streaming_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }

  prepared_ = true;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  session_id_ = request.session_id();
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
  if (request.session_id().empty() || ticket.ticket_hash().size() != 32 ||
      ticket.client_id().empty() || ticket.plan_revision() == 0 ||
      ticket.expires_at_unix_ms() == 0 ||
      ticket.worker_instance_id() != bytes_to_string(worker_instance_id_)) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_REQUEST);
  }

  TicketHash hash{};
  for (std::size_t index = 0; index < hash.size(); ++index) {
    hash[index] = static_cast<std::byte>(ticket.ticket_hash()[index]);
  }
  if (!authorized_tickets_.authorize({
          .hash = hash,
          .client_id = ticket.client_id(),
          .session_id = request.session_id(),
          .plan_revision = ticket.plan_revision(),
          .expires_at_unix_ms = ticket.expires_at_unix_ms(),
          .benchmark_plan = benchmark_prepared_ &&
                                    request.session_id() == session_id_
                                ? std::optional{benchmark_plan_}
                                : std::nullopt,
      })) {
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
  transport_.close_connection();
  streaming_ = false;
  prepared_ = false;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_STOPPED);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  return {std::move(state), completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::shutdown(
    const v1::WorkerIpcEnvelope& request) {
  if (streaming_) {
    transport_.close_connection();
  }
  transport_.shutdown();
  streaming_ = false;
  prepared_ = false;
  benchmark_prepared_ = false;
  benchmark_plan_.Clear();
  shutdown_requested_ = true;
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

}  // namespace beacon::worker
