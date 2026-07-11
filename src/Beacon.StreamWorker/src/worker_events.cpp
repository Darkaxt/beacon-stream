#include "beacon/worker/worker_events.h"

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

v1::WorkerIpcEnvelope make_transport_authenticated_event(
    std::string_view session_id, std::uint64_t session_generation,
    std::uint16_t maximum_datagram_bytes) {
  auto event = event_envelope(session_id);
  auto *body = event.mutable_transport_authenticated();
  body->set_session_generation(session_generation);
  body->set_maximum_datagram_bytes(maximum_datagram_bytes);
  return event;
}

v1::WorkerIpcEnvelope make_transport_disconnected_event(
    std::string_view session_id, std::uint64_t session_generation) {
  auto event = event_envelope(session_id);
  event.mutable_transport_disconnected()->set_session_generation(
      session_generation);
  return event;
}

v1::WorkerIpcEnvelope make_input_received_event(
    std::uint64_t session_generation,
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

} // namespace beacon::worker
