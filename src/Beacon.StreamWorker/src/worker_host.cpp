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
                       stream::IStreamTransport& transport)
    : worker_instance_id_(std::move(worker_instance_id)),
      process_id_(process_id),
      transport_(transport) {}

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
  session_id_ = request.session_id();
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_PREPARED);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  return {std::move(state), completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

std::vector<v1::WorkerIpcEnvelope> WorkerHost::start_media(
    const v1::WorkerIpcEnvelope& request) {
  if (!prepared_ || streaming_ || request.session_id() != session_id_) {
    return reject(request, v1::WORKER_ERROR_CODE_INVALID_STATE);
  }
  if (!transport_.open_connection()) {
    return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
  }

  for (std::uint64_t sequence = 1; sequence <= 3; ++sequence) {
    stream::TransportPacket packet{
        .channel = stream::StreamChannel::media,
        .sequence = sequence,
        .payload = {std::byte{'B'}, static_cast<std::byte>(sequence)},
    };
    if (transport_.send(std::move(packet)) != stream::TransportSendResult::accepted) {
      transport_.close_connection();
      return reject(request, v1::WORKER_ERROR_CODE_OPERATION_FAILED);
    }
  }

  streaming_ = true;
  auto state = response_envelope(request);
  state.mutable_session_state_changed()->set_state(v1::WORKER_SESSION_STATE_STREAMING);
  state.mutable_session_state_changed()->set_error_code(v1::WORKER_ERROR_CODE_NONE);
  auto metrics = response_envelope(request);
  metrics.mutable_media_metrics()->set_encoded_frames(3);
  metrics.mutable_media_metrics()->set_sent_datagrams(3);
  metrics.mutable_media_metrics()->set_bytes_sent(6);
  return {std::move(state), std::move(metrics),
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
  shutdown_requested_ = true;
  return {completion(request, true, v1::WORKER_ERROR_CODE_NONE)};
}

}  // namespace beacon::worker
