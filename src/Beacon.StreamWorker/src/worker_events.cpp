#include "beacon/worker/worker_events.h"

#include <utility>

namespace beacon::worker {
namespace {

v1::WorkerIpcEnvelope event_envelope(std::string_view session_id) {
  v1::WorkerIpcEnvelope event;
  event.set_protocol_version(1);
  event.set_request_id(0);
  event.set_session_id(session_id);
  return event;
}

} // namespace

v1::WorkerIpcEnvelope
make_connection_observed_event(std::uint64_t connection_generation) {
  auto event = event_envelope({});
  auto *body = event.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_INFORMATION);
  body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
  body->set_code(v1::DIAGNOSTIC_CODE_CONNECTION_OBSERVED);
  body->set_numeric_value(connection_generation);
  return event;
}

v1::WorkerIpcEnvelope
make_connection_configured_event(std::uint64_t connection_generation) {
  auto event = event_envelope({});
  auto *body = event.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_INFORMATION);
  body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
  body->set_code(v1::DIAGNOSTIC_CODE_CONNECTION_CONFIGURED);
  body->set_numeric_value(connection_generation);
  return event;
}

v1::WorkerIpcEnvelope
make_transport_connected_event(std::uint64_t connection_generation) {
  auto event = event_envelope({});
  auto *body = event.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_INFORMATION);
  body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
  body->set_code(v1::DIAGNOSTIC_CODE_TRANSPORT_CONNECTED);
  body->set_numeric_value(connection_generation);
  return event;
}

v1::WorkerIpcEnvelope
make_transport_failed_event(std::uint64_t connection_generation,
                            std::uint32_t platform_status_code) {
  auto event = event_envelope({});
  auto *body = event.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_INFORMATION);
  body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
  body->set_code(v1::DIAGNOSTIC_CODE_TRANSPORT_FAILED);
  body->set_platform_error_code(platform_status_code);
  body->set_numeric_value(connection_generation);
  return event;
}

v1::WorkerIpcEnvelope
make_transport_authenticated_event(std::string_view session_id,
                                   std::uint64_t session_generation,
                                   std::uint16_t maximum_datagram_bytes) {
  auto event = event_envelope(session_id);
  auto *body = event.mutable_transport_authenticated();
  body->set_session_generation(session_generation);
  body->set_maximum_datagram_bytes(maximum_datagram_bytes);
  return event;
}

v1::WorkerIpcEnvelope
make_transport_disconnected_event(std::string_view session_id,
                                  std::uint64_t session_generation) {
  auto event = event_envelope(session_id);
  event.mutable_transport_disconnected()->set_session_generation(
      session_generation);
  return event;
}

v1::WorkerIpcEnvelope
make_input_received_event(std::uint64_t session_generation,
                          const stream::v1::InputStreamEnvelope &input) {
  auto event = event_envelope(input.session_id());
  auto *body = event.mutable_input_received();
  body->set_session_generation(session_generation);
  *body->mutable_input() = input;
  return event;
}

v1::WorkerIpcEnvelope make_feedback_received_event(
    std::uint64_t session_generation,
    const stream::v1::FeedbackStreamEnvelope &feedback) {
  auto event = event_envelope(feedback.session_id());
  auto *body = event.mutable_feedback_received();
  body->set_session_generation(session_generation);
  *body->mutable_feedback() = feedback;
  return event;
}

v1::WorkerIpcEnvelope make_media_evidence_event(
    std::string_view session_id, std::uint64_t session_generation,
    std::uint64_t sequence, std::uint64_t presentation_time_us,
    std::uint32_t datagram_bytes) {
  auto event = event_envelope(session_id);
  auto *body = event.mutable_media_evidence();
  body->set_session_generation(session_generation);
  body->set_sequence(sequence);
  body->set_presentation_time_us(presentation_time_us);
  body->set_datagram_bytes(datagram_bytes);
  return event;
}

std::vector<v1::WorkerIpcEnvelope> make_video_pipeline_failure_events(
    const video::VideoPipelineFailureEvent &failure) {
  auto state = event_envelope(failure.session_id);
  state.mutable_session_state_changed()->set_state(
      v1::WORKER_SESSION_STATE_FAILED);
  state.mutable_session_state_changed()->set_error_code(
      v1::WORKER_ERROR_CODE_OPERATION_FAILED);

  auto diagnostic = event_envelope(failure.session_id);
  auto *body = diagnostic.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_ERROR);
  switch (failure.boundary) {
  case video::VideoPipelineFailureBoundary::capture:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_CAPTURE);
    break;
  case video::VideoPipelineFailureBoundary::transport:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
    break;
  case video::VideoPipelineFailureBoundary::video_processor:
  case video::VideoPipelineFailureBoundary::encoder:
  case video::VideoPipelineFailureBoundary::media_session:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_ENCODER);
    break;
  }
  body->set_code(v1::DIAGNOSTIC_CODE_OPERATION_FAILED);
  body->set_platform_error_code(failure.native_code);
  body->set_numeric_value(failure.session_generation);
  body->set_failure_stage(failure.failure_stage);
  return {std::move(state), std::move(diagnostic)};
}

std::vector<v1::WorkerIpcEnvelope> make_audio_pipeline_failure_events(
    const audio::AudioPipelineFailureEvent &failure) {
  auto state = event_envelope(failure.session_id);
  state.mutable_session_state_changed()->set_state(
      v1::WORKER_SESSION_STATE_FAILED);
  state.mutable_session_state_changed()->set_error_code(
      v1::WORKER_ERROR_CODE_OPERATION_FAILED);

  auto diagnostic = event_envelope(failure.session_id);
  auto *body = diagnostic.mutable_worker_diagnostic();
  body->set_severity(v1::DIAGNOSTIC_SEVERITY_ERROR);
  switch (failure.boundary) {
  case audio::AudioPipelineFailureBoundary::capture:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_AUDIO_CAPTURE);
    break;
  case audio::AudioPipelineFailureBoundary::encoder:
  case audio::AudioPipelineFailureBoundary::media_session:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_AUDIO_ENCODER);
    break;
  case audio::AudioPipelineFailureBoundary::transport:
    body->set_boundary(v1::DIAGNOSTIC_BOUNDARY_TRANSPORT);
    break;
  }
  body->set_code(v1::DIAGNOSTIC_CODE_OPERATION_FAILED);
  body->set_platform_error_code(failure.native_code);
  body->set_numeric_value(failure.session_generation);
  body->set_failure_stage(failure.failure_stage);
  return {std::move(state), std::move(diagnostic)};
}

} // namespace beacon::worker
